using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using NLog;
using VaultClientIntegrationLib;
using VaultClientOperationsLib;
using VaultLib;

namespace Vault2Git.Lib
{
    public class Processor
    {
        #region constants

        //git commands
        private const string GIT_GC_CMD = "gc --auto";
        private const string GIT_FINALIZER = "update-server-info";
        private const string GIT_ADD_CMD = "add --all .";
        private const string GIT_STATUS_CMD = "status --porcelain";
        private const string GIT_LAST_COMMIT_INFO_CMD = "log -1 {0}";
        private const string GIT_COMMIT_CMD = @"commit --allow-empty --all --date=""{2}"" --author=""{0} <{0}@{1}>"" -F -";
        private const string GIT_CHECKOUT_CMD = "checkout --quiet --force {0}";
        private const string GIT_BRANCH_CMD = "branch";
        private const string GIT_ADD_TAG_CMD = @"tag {0} {1} -a -m ""{2}""";

        //private vars

        //constants
        private const string VAULT_TAG = "[git-vault-id]";

        /// <summary>
        ///     version number reported to <see cref="Progress" /> when init is complete
        /// </summary>
        public const int PROGRESS_SPECIAL_VERSION_INIT = 0;

        /// <summary>
        ///     version number reported to <see cref="Progress" /> when git gc is complete
        /// </summary>
        public const int PROGRESS_SPECIAL_VERSION_GC = -1;

        /// <summary>
        ///     version number reported to <see cref="Progress" /> when finalization finished (e.g. logout, unset wf etc)
        /// </summary>
        public const int PROGRESS_SPECIAL_VERSION_FINALIZE = -2;

        /// <summary>
        ///     version number reported to <see cref="Progress" /> when git tags creation is completed
        /// </summary>
        public const int PROGRESS_SPECIAL_VERSION_TAGS = -3;

        #endregion

        private static Logger _logger = LogManager.GetCurrentClassLogger();
        public int GitGarbageCollectionInterval = 200;

        public bool SkipEmptyCommits = false;

        /// <summary>
        ///     Maps Vault TransactionID to Git Commit SHA-1 Hash
        /// </summary>
        private IDictionary<long, String> _txidMappings = new Dictionary<long, String>();

        /// <summary>
        ///     path where conversion will take place. If it not already set as value working folder, it will be set automatically
        /// </summary>
        public string WorkingFolder { get; set; }

        public string VaultPassword { get; set; }
        public string VaultRepository { get; set; }
        public string VaultServer { get; set; }
        public string VaultUser { get; set; }

        /// <summary>
        ///     path to git.exe
        /// </summary>
        public string GitCmd { get; set; }

        public string GitDomainName { get; set; }
        public Func<long, int, bool> Progress { get; set; }


        /// <summary>
        ///     Pulls versions
        /// </summary>
        /// <param name="git2VaultRepoPath">Key=git, Value=vault</param>
        /// <param name="limitCount"></param>
        /// <returns></returns>
        public bool Pull(IEnumerable<KeyValuePair<string, string>> git2VaultRepoPath, long limitCount)
        {
            int ticks = 0;
            //get git current branch
            string gitCurrentBranch;
            ticks += GitCurrentBranch(out gitCurrentBranch);

            //reorder target branches to start from current (to avoid checkouts)
            var targetList =
                git2VaultRepoPath.OrderByDescending(p => p.Key.Equals(gitCurrentBranch, StringComparison.CurrentCultureIgnoreCase));

            ticks += VaultLogin();
            try
            {
                foreach (var pair in targetList)
                {
                    var gitBranch = pair.Key;
                    var vaultRepoPath = pair.Value;

                    long currentGitVaultVersion = 0;

                    //reset ticks
                    ticks = 0;

                    //get current version
                    ticks += GitVaultVersion(gitBranch, out currentGitVaultVersion);

                    //get vaultVersions
                    IDictionary<long, VaultVersionInfo> vaultVersions = new SortedList<long, VaultVersionInfo>();

                    ticks += vaultPopulateInfo(vaultRepoPath, vaultVersions);

                    var versionsToProcess = vaultVersions.Where(p => p.Key > currentGitVaultVersion).ToList();

                    //do init only if there is something to work on
                    if (versionsToProcess.Any())
                    {
                        ticks += Init(vaultRepoPath, gitBranch);
                    }

                    //report init
                    if (null != Progress)
                        if (Progress(PROGRESS_SPECIAL_VERSION_INIT, ticks))
                            return true;

                    var counter = 0;
                    foreach (var version in versionsToProcess)
                    {
                        //get vault version
                        ticks = VaultGet(vaultRepoPath, version.Key, version.Value.TrxId);
                        //change all sln files
                        Directory.GetFiles(
                            WorkingFolder,
                            "*.sln",
                            SearchOption.AllDirectories)
                            //remove temp files created by vault
                                 .Where(f => !f.Contains("~"))
                                 .ToList()
                                 .ForEach(f => ticks += RemoveSccFromSln(f));
                        //change all csproj files
                        Directory.GetFiles(
                            WorkingFolder,
                            "*.csproj",
                            SearchOption.AllDirectories)
                            //remove temp files created by vault
                                 .Where(f => !f.Contains("~"))
                                 .ToList()
                                 .ForEach(f => ticks += RemoveSccFromCsProj(f));
                        //change all vdproj files
                        Directory.GetFiles(
                            WorkingFolder,
                            "*.vdproj",
                            SearchOption.AllDirectories)
                            //remove temp files created by vault
                                 .Where(f => !f.Contains("~"))
                                 .ToList()
                                 .ForEach(f => ticks += RemoveSccFromVdProj(f));
                        //get vault version info
                        var info = vaultVersions[version.Key];
                        //commit
                        ticks += GitCommit(info.Login, info.TrxId, GitDomainName,
                            BuildCommitMessage(vaultRepoPath, version.Key, info), info.TimeStamp);

                        if (null != Progress)
                            if (Progress(version.Key, ticks))
                                return true;
                        counter++;

                        //call gc
                        if (0 == counter%GitGarbageCollectionInterval)
                        {
                            _logger.Debug(String.Format("interval {0} reached. calling garbage collector", GitGarbageCollectionInterval));
                            ticks = GitGc();
                            if (null != Progress)
                                if (Progress(PROGRESS_SPECIAL_VERSION_GC, ticks))
                                    return true;
                        }

                        //check if limit is reached
                        if (counter >= limitCount)
                        {
                            _logger.Info("limit reached. exiting...");
                            break;
                        }
                    }
                    ticks = VaultFinalize(vaultRepoPath);
                }
            }
            finally
            {
                //complete
                ticks += VaultLogout();
                //finalize git (update server info for dumb clients)
                ticks += GitFinalize();
                if (null != Progress)
                    Progress(PROGRESS_SPECIAL_VERSION_FINALIZE, ticks);
            }
            return false;
        }


        /// <summary>
        ///     removes Source control refs from sln files
        /// </summary>
        /// <param name="filePath">path to sln file</param>
        /// <returns></returns>
        private static int RemoveSccFromSln(string filePath)
        {
            var ticks = Environment.TickCount;
            var lines = File.ReadAllLines(filePath).ToList();
            //scan lines 
            var searchingForStart = true;
            var beginingLine = 0;
            var endingLine = 0;
            var currentLine = 0;
            foreach (var trimmedLine in lines.Select(line => line.Trim()))
            {
                if (searchingForStart)
                {
                    if (trimmedLine.StartsWith("GlobalSection(SourceCodeControl)"))
                    {
                        beginingLine = currentLine;
                        searchingForStart = false;
                    }
                }
                else
                {
                    if (trimmedLine.StartsWith("EndGlobalSection"))
                    {
                        endingLine = currentLine;
                        break;
                    }
                }
                currentLine++;
            }
            //removing lines
            if (beginingLine > 0 & endingLine > 0)
            {
                lines.RemoveRange(beginingLine, endingLine - beginingLine + 1);
                File.WriteAllLines(filePath, lines.ToArray(), Encoding.UTF8);
            }
            return Environment.TickCount - ticks;
        }

        /// <summary>
        ///     removes Source control refs from csProj files
        /// </summary>
        /// <param name="filePath">path to sln file</param>
        /// <returns></returns>
        public static int RemoveSccFromCsProj(string filePath)
        {
            var ticks = Environment.TickCount;
            var doc = new XmlDocument();
            try
            {
                doc.Load(filePath);
                while (true)
                {
                    var nav = doc.CreateNavigator().SelectSingleNode("//*[starts-with(name(), 'Scc')]");
                    if (null == nav)
                        break;
                    nav.DeleteSelf();
                }
                doc.Save(filePath);
            }
            catch (Exception exception)
            {
                _logger.Error("Failed to remove SCC from csproj for {0}.\nException {1}", filePath, exception.GetBaseException());
            }
            return Environment.TickCount - ticks;
        }

        /// <summary>
        ///     removes Source control refs from vdProj files
        /// </summary>
        /// <param name="filePath">path to sln file</param>
        /// <returns></returns>
        private static int RemoveSccFromVdProj(string filePath)
        {
            var ticks = Environment.TickCount;
            var lines = File.ReadAllLines(filePath).ToList();
            File.WriteAllLines(filePath, lines.Where(l => !l.Trim().StartsWith(@"""Scc")).ToArray(), Encoding.UTF8);
            return Environment.TickCount - ticks;
        }

        private int vaultPopulateInfo(string repoPath, IDictionary<long, VaultVersionInfo> info)
        {
            var ticks = Environment.TickCount;

            foreach (var i in ServerOperations.ProcessCommandVersionHistory(repoPath,
                1,
                VaultDateTime.Parse("2000-01-01"),
                VaultDateTime.Parse("2020-01-01"),
                0))
                info.Add(i.Version, new VaultVersionInfo
                                    {
                                        TrxId = i.TxID,
                                        Comment = i.Comment,
                                        Login = i.UserLogin,
                                        TimeStamp = i.TxDate.GetDateTime()
                                    });
            return Environment.TickCount - ticks;
        }

        /// <summary>
        ///     Creates Git tags from Vault labels
        /// </summary>
        /// <returns></returns>
        public bool CreateTagsFromLabels()
        {
            VaultLogin();

            // Search for all labels recursively
            const string REPOSITORY_FOLDER_PATH = "$";

            long objId = RepositoryUtil.FindVaultTreeObjectAtReposOrLocalPath(REPOSITORY_FOLDER_PATH).ID;
            string qryToken;
            long rowsRetMain;
            long rowsRetRecur;

            VaultLabelItemX[] labelItems;

            ServerOperations.client.ClientInstance.BeginLabelQuery(REPOSITORY_FOLDER_PATH,
                objId,
                true, // get recursive
                true, // get inherited
                true, // get file items
                true, // get folder items
                0, // no limit on results
                out rowsRetMain,
                out rowsRetRecur,
                out qryToken);


            ServerOperations.client.ClientInstance.GetLabelQueryItems_Recursive(qryToken,
                0,
                (int) rowsRetRecur,
                out labelItems);
            try
            {
                int ticks = 0;

                foreach (VaultLabelItemX currItem in labelItems)
                {
                    if (!_txidMappings.ContainsKey(currItem.TxID))
                        continue;

                    string gitCommitId = _txidMappings.First(s => s.Key.Equals(currItem.TxID)).Value;

                    if (!string.IsNullOrEmpty(gitCommitId))
                    {
                        string gitLabelName = Regex.Replace(currItem.Label, "[\\W]", "_");
                        ticks += GitAddTag(currItem.TxID + "_" + gitLabelName, gitCommitId, currItem.Comment);
                    }
                }

                //add ticks for git tags
                if (null != Progress)
                    Progress(PROGRESS_SPECIAL_VERSION_TAGS, ticks);
            }
            finally
            {
                //complete
                ServerOperations.client.ClientInstance.EndLabelQuery(qryToken);
                VaultLogout();
                GitFinalize();
            }
            return true;
        }

        private int VaultGet(string repoPath, long version, long txId)
        {
            var ticks = Environment.TickCount;
            //apply version to the repo folder
            try
            {
                GetOperations.ProcessCommandGetVersion(
                    repoPath,
                    Convert.ToInt32(version),
                    new GetOptions
                    {
                        MakeWritable = MakeWritableType.MakeAllFilesWritable,
                        Merge = MergeType.OverwriteWorkingCopy,
                        OverrideEOL = VaultEOL.None,
                        //remove working copy does not work -- bug http://support.sourcegear.com/viewtopic.php?f=5&t=11145
                        PerformDeletions = PerformDeletionsType.RemoveWorkingCopy,
                        SetFileTime = SetFileTimeType.Current,
                        Recursive = true
                    });
            }
            catch (Exception exception)
            {
                _logger.Error(String.Format("Exception occurred when grabbing from vault.\nException {0}", exception.GetBaseException()));
                // System.Exception: $/foo/bar/baz has no working folder set.
                // happens if a directory name changed. 
                // therefore, if an Exception happened try to get the commit outside of the working folder.
                GetOperations.ProcessCommandGetVersionToLocationOutsideWorkingFolder(
                    repoPath,
                    Convert.ToInt32(version),
                    new GetOptions
                    {
                        MakeWritable = MakeWritableType.MakeAllFilesWritable,
                        Merge = MergeType.OverwriteWorkingCopy,
                        OverrideEOL = VaultEOL.None,
                        //remove working copy does not work -- bug http://support.sourcegear.com/viewtopic.php?f=5&t=11145
                        PerformDeletions = PerformDeletionsType.RemoveWorkingCopy,
                        SetFileTime = SetFileTimeType.Current,
                        Recursive = true
                    },
                    WorkingFolder);
            }

            //now process deletions, moves, and renames (due to vault bug)
            var allowedRequests = new[]
                                  {
                                      9, //delete
                                      12, //move
                                      15 //rename
                                  };
            foreach (var item in ServerOperations.ProcessCommandTxDetail(txId).items
                                                 .Where(i => allowedRequests.Contains(i.RequestType)))

                //delete file
                //check if it is within current branch
                if (item.ItemPath1.StartsWith(repoPath, StringComparison.CurrentCultureIgnoreCase))
                {
                    string pathToDelete = null;
                    if (!string.IsNullOrEmpty(item.ItemPath1) && item.ItemPath1.Length >= repoPath.Length + 1)
                    {
                        pathToDelete = Path.Combine(WorkingFolder, item.ItemPath1.Substring(repoPath.Length + 1));
                    }

                    if (pathToDelete != null && File.Exists(pathToDelete))
                    {
                        try
                        {
                            File.Delete(pathToDelete);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            /* don't worry about it */
                        }
                    }
                    if (pathToDelete != null && Directory.Exists(pathToDelete))
                    {
                        try
                        {
                            Directory.Delete(pathToDelete, true);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            /* don't worry about it */
                        }
                    }
                }
            return Environment.TickCount - ticks;
        }

        private int GitVaultVersion(string gitBranch, out long currentVersion)
        {
            string[] msgs;
            //get info
            var ticks = GitLog(gitBranch, out msgs);
            //get vault version
            currentVersion = getVaultVersionFromGitLogMessage(msgs);
            return ticks;
        }

        private int Init(string vaultRepoPath, string gitBranch)
        {
            //set working folder
            var ticks = SetVaultWorkingFolder(vaultRepoPath);
            //checkout branch
            for (int tries = 0;; tries++)
            {
                string[] msgs;
                ticks += RunGitCommand(string.Format(GIT_CHECKOUT_CMD, gitBranch), string.Empty, out msgs);
                //confirm current branch (sometimes checkout failed)
                string currentBranch;
                ticks += GitCurrentBranch(out currentBranch);
                if (gitBranch.Equals(currentBranch, StringComparison.OrdinalIgnoreCase))
                    break;
                if (tries > 5)
                    throw new Exception("cannot switch");
            }
            return ticks;
        }

        private int VaultFinalize(string vaultRepoPath)
        {
            //unset working folder
            return unSetVaultWorkingFolder(vaultRepoPath);
        }

        private int GitCommit(string vaultLogin, long vaultTrxid, string gitDomainName, string vaultCommitMessage, DateTime commitTimeStamp)
        {
            string gitCurrentBranch;
            GitCurrentBranch(out gitCurrentBranch);

            string[] msgs;
            var ticks = RunGitCommand(GIT_ADD_CMD, string.Empty, out msgs);
            if (SkipEmptyCommits)
            {
                //checking status
                ticks += RunGitCommand(
                    GIT_STATUS_CMD,
                    string.Empty,
                    out msgs
                    );
                if (!msgs.Any())
                    return ticks;
            }
            ticks += RunGitCommand(
                string.Format(GIT_COMMIT_CMD, vaultLogin, gitDomainName, string.Format("{0:s}", commitTimeStamp)),
                vaultCommitMessage,
                out msgs
                );

            // Mapping Vault Transaction ID to Git Commit SHA-1 Hash
            if (msgs[0].StartsWith("[" + gitCurrentBranch))
            {
                string gitCommitId = msgs[0].Split(' ')[1];
                gitCommitId = gitCommitId.Substring(0, gitCommitId.Length - 1);
                _txidMappings.Add(vaultTrxid, gitCommitId);
            }
            return ticks;
        }

        private int GitCurrentBranch(out string currentBranch)
        {
            string[] msgs;
            var ticks = RunGitCommand(GIT_BRANCH_CMD, string.Empty, out msgs);
            currentBranch = msgs.First(s => s.StartsWith("*")).Substring(1).Trim();
            return ticks;
        }

        private string BuildCommitMessage(string repoPath, long version, VaultVersionInfo info)
        {
            //parse path repo$RepoPath@version/trx
            var r = new StringBuilder(info.Comment);
            r.AppendLine();
            r.AppendFormat("{4} {0}{1}@{2}/{3}", VaultRepository, repoPath, version, info.TrxId, VAULT_TAG);
            r.AppendLine();
            return r.ToString();
        }

        private long getVaultVersionFromGitLogMessage(string[] msg)
        {
            //get last string
            var stringToParse = msg.Last();
            //search for version tag
            var versionString = stringToParse.Split(new[] {VAULT_TAG}, StringSplitOptions.None).LastOrDefault();
            if (null == versionString)
                return 0;
            //parse path reporepoPath@version/trx
            //get version/trx part
            var versionTrxTag = versionString.Split('@').LastOrDefault();
            if (null == versionTrxTag)
                return 0;

            //get version
            long version;
            long.TryParse(versionTrxTag.Split('/').First(), out version);
            return version;
        }

        private int GitLog(string gitBranch, out string[] msg)
        {
            return RunGitCommand(string.Format(GIT_LAST_COMMIT_INFO_CMD, gitBranch), string.Empty, out msg);
        }

        private int GitAddTag(string gitTagName, string gitCommitId, string gitTagComment)
        {
            string[] msg;
            return RunGitCommand(string.Format(GIT_ADD_TAG_CMD, gitTagName, gitCommitId, gitTagComment),
                string.Empty,
                out msg);
        }

        private int GitGc()
        {
            string[] msg;
            return RunGitCommand(GIT_GC_CMD, string.Empty, out msg);
        }

        private int GitFinalize()
        {
            string[] msg;
            return RunGitCommand(GIT_FINALIZER, string.Empty, out msg);
        }

        private int SetVaultWorkingFolder(string repoPath)
        {
            var ticks = Environment.TickCount;
            ServerOperations.SetWorkingFolder(repoPath, WorkingFolder, true);
            return Environment.TickCount - ticks;
        }

        private int unSetVaultWorkingFolder(string repoPath)
        {
            var ticks = Environment.TickCount;
            //remove any assignment first
            //it is case sensitive, so we have to find how it is recorded first
            var exPath = ServerOperations.GetWorkingFolderAssignments()
                                         .Cast<DictionaryEntry>()
                                         .Select(e => e.Key.ToString()).FirstOrDefault(e => repoPath.Equals(e, StringComparison.OrdinalIgnoreCase));
            if (null != exPath)
                ServerOperations.RemoveWorkingFolder(exPath);
            return Environment.TickCount - ticks;
        }

        private int RunGitCommand(string cmd, string stdInput, out string[] stdOutput)
        {
            return RunGitCommand(cmd, stdInput, out stdOutput, null);
        }

        private int RunGitCommand(string cmd, string stdInput, out string[] stdOutput, IDictionary<string, string> env)
        {
            var ticks = Environment.TickCount;

            var pi = new ProcessStartInfo(GitCmd, cmd)
                     {
                         WorkingDirectory = WorkingFolder,
                         UseShellExecute = false,
                         RedirectStandardOutput = true,
                         RedirectStandardInput = true
                     };
            //set env vars
            if (null != env)
                foreach (var e in env)
                    pi.EnvironmentVariables.Add(e.Key, e.Value);
            using (var p = new Process
                           {
                               StartInfo = pi
                           })
            {
                p.Start();
                p.StandardInput.Write(stdInput);
                p.StandardInput.Close();
                var msgs = new List<string>();
                while (!p.StandardOutput.EndOfStream)
                    msgs.Add(p.StandardOutput.ReadLine());
                stdOutput = msgs.ToArray();
                p.WaitForExit();
            }
            return Environment.TickCount - ticks;
        }

        private int VaultLogin()
        {
            var ticks = Environment.TickCount;
            ServerOperations.client.LoginOptions.URL = string.Format("http://{0}/VaultService", VaultServer);
            ServerOperations.client.LoginOptions.User = VaultUser;
            ServerOperations.client.LoginOptions.Password = VaultPassword;
            ServerOperations.client.LoginOptions.Repository = VaultRepository;
            ServerOperations.Login();
            ServerOperations.client.MakeBackups = false;
            ServerOperations.client.AutoCommit = false;
            ServerOperations.client.Verbose = true;
            return Environment.TickCount - ticks;
        }

        private static int VaultLogout()
        {
            var ticks = Environment.TickCount;
            ServerOperations.Logout();
            return Environment.TickCount - ticks;
        }

        private struct VaultVersionInfo
        {
            public string Comment;
            public string Login;
            public DateTime TimeStamp;
            public long TrxId;
        }
    }
}