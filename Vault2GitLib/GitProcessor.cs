using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VaultClientOperationsLib;

namespace Vault2Git.Lib
{
    internal class GitProcessor
    {
        private const string GIT_GC_CMD = "gc --auto";
        private const string GIT_FINALIZER = "update-server-info";
        public const string GIT_ADD_CMD = "add --all .";
        public const string GIT_STATUS_CMD = "status --porcelain";
        private const string GIT_LAST_COMMIT_INFO_CMD = "log -1 {0}";
        public const string GIT_COMMIT_CMD = @"commit --allow-empty --all --date=""{2}"" --author=""{0} <{0}@{1}>"" -F -";
        public const string GIT_CHECKOUT_CMD = "checkout --quiet --force {0}";
        public const string GIT_BRANCH_CMD = "branch";
        public const string GIT_ADD_TAG_CMD = @"tag {0} {1} -a -m ""{2}""";

        internal int GitAddTag(Processor processor, string gitTagName, string gitCommitId, string gitTagComment)
        {
            string[] msg;
            return RunGitCommand(processor.WorkingFolder, processor.GitCmd, string.Format(GitProcessor.GIT_ADD_TAG_CMD, gitTagName, gitCommitId, gitTagComment),
                string.Empty,
                out msg);
        }

        internal int GitCurrentBranch(String gitCommand, String workingFolder, out string currentBranch)
        {
            string[] msgs;
            var ticks = RunGitCommand(workingFolder, gitCommand, GIT_BRANCH_CMD, string.Empty, out msgs);
            currentBranch = msgs.First(s => s.StartsWith("*")).Substring(1).Trim();
            return ticks;
        }

        internal int GitFinalize(string gitCommand, string workingFolder)
        {
            string[] msg;
            return RunGitCommand(workingFolder, gitCommand, GIT_FINALIZER, string.Empty, out msg);
        }

        internal int GitTriggerGarbageCollection(string gitCommand, string workingFolder)
        {
            string[] msg;
            return RunGitCommand(workingFolder, gitCommand, GIT_GC_CMD, string.Empty, out msg);
        }

        internal int GitLog(Processor processor, string gitBranch, out string[] msg)
        {
            return RunGitCommand(processor.WorkingFolder, processor.GitCmd, string.Format(GIT_LAST_COMMIT_INFO_CMD, gitBranch), string.Empty, out msg);
        }

        internal int RunGitCommand(string workingFolder, string gitCommand, string cmd, string stdInput, out string[] stdOutput)
        {
            return RunGitCommand(cmd, stdInput, out stdOutput, null, gitCommand, workingFolder);
        }

        internal int RunGitCommand(string cmd, string stdInput, out string[] stdOutput,
            IDictionary<string, string> env, String gitCommand, String workingFolder)
        {
            var ticks = Environment.TickCount;

            var pi = new ProcessStartInfo(gitCommand, cmd)
                     {
                         WorkingDirectory = workingFolder,
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
    }
}