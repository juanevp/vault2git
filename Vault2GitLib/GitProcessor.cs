using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Vault2Git.Lib
{
    internal class GitProcessor
    {
        public const string GIT_GC_CMD = "gc --auto";
        public const string GIT_FINALIZER = "update-server-info";
        public const string GIT_ADD_CMD = "add --all .";
        public const string GIT_STATUS_CMD = "status --porcelain";
        public const string GIT_LAST_COMMIT_INFO_CMD = "log -1 {0}";
        public const string GIT_COMMIT_CMD = @"commit --allow-empty --all --date=""{2}"" --author=""{0} <{0}@{1}>"" -F -";
        public const string GIT_CHECKOUT_CMD = "checkout --quiet --force {0}";
        public const string GIT_BRANCH_CMD = "branch";
        public const string GIT_ADD_TAG_CMD = @"tag {0} {1} -a -m ""{2}""";

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