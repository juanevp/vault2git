using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using NLog;
using Vault2Git.Lib;

namespace Vault2Git.CLI
{
    internal static class Program
    {
        private static bool _useCapsLock;
        private static bool _useConsole;
        private static bool _ignoreLabels;
        private static Logger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        ///     The main entry point for the application.
        /// </summary>
        //[STAThread]
        private static void Main(string[] args)
        {
            try
            {
                _logger.Info("Vault2Git -- converting history from Vault repositories to Git");
                Console.InputEncoding = Encoding.UTF8;

                //get configuration for branches
                var paths = ConfigurationManager.AppSettings["Convertor.Paths"];
                var pathPairs = paths.Split(';')
                                     .ToDictionary(
                                         pair =>
                                             pair.Split('~')[1], pair => pair.Split('~')[0]
                    );

                //parse params
                var param = Params.Parse(args, pathPairs.Keys);

                //get count from param
                if (param.Errors.Any())
                {
                    foreach (var e in param.Errors)
                        _logger.Error(e);
                    return;
                }

                _logger.Info("   use Vault2Git --help to get additional info");

                _useConsole = param.UseConsole;
                _useCapsLock = param.UseCapsLock;
                _ignoreLabels = param.IgnoreLabels;


                _logger.Debug("initialising processor.");
                var processor = new Processor
                                {
                                    WorkingFolder = ConfigurationManager.AppSettings["Convertor.WorkingFolder"],
                                    GitCmd = ConfigurationManager.AppSettings["Convertor.GitCmd"],
                                    GitDomainName = ConfigurationManager.AppSettings["Git.DomainName"],
                                    VaultServer = ConfigurationManager.AppSettings["Vault.Server"],
                                    VaultRepository = ConfigurationManager.AppSettings["Vault.Repo"],
                                    VaultUser = ConfigurationManager.AppSettings["Vault.User"],
                                    VaultPassword = ConfigurationManager.AppSettings["Vault.Password"],
                                    Progress = ShowProgress,
                                    SkipEmptyCommits = param.SkipEmptyCommits
                                };

                _logger.Debug("pulling request");
                processor.Pull
                    (
                        pathPairs.Where(p => param.Branches.Contains(p.Key))
                        , 0 == param.Limit ? 999999999 : param.Limit
                    );

                if (!_ignoreLabels)
                {
                    _logger.Debug("creating tags from labels");
                    processor.CreateTagsFromLabels();
                }

                _logger.Info("Vault2Git life ended. Game over.");
            }
            catch (Exception exception)
            {
                _logger.Error(string.Format("Exception occurred in main program. Exception : {0}\nStackTrace: {1}",
                    exception.GetBaseException().Message, exception.StackTrace));
            }
        }

        private static bool ShowProgress(long version, int ticks)
        {
            var timeSpan = TimeSpan.FromMilliseconds(ticks);
            if (_useConsole)
            {
                switch (version)
                {
                    case Processor.PROGRESS_SPECIAL_VERSION_INIT:
                        _logger.Info("init took {0}", timeSpan);
                        break;
                    case Processor.PROGRESS_SPECIAL_VERSION_GC:
                        _logger.Info("gc took {0}", timeSpan);
                        break;
                    case Processor.PROGRESS_SPECIAL_VERSION_FINALIZE:
                        _logger.Info("finalization took {0}", timeSpan);
                        break;
                    case Processor.PROGRESS_SPECIAL_VERSION_TAGS:
                        _logger.Info("tags creation took {0}", timeSpan);
                        break;
                    default:
                        _logger.Info("processing version {0} took {1}", version, timeSpan);
                        break;
                }
            }
            return _useCapsLock && Console.CapsLock; //cancel flag
        }

        private class Params
        {
            private const string LIMIT_PARAM = "--limit=";
            private const string BRANCH_PARAM = "--branch=";
            public IEnumerable<string> Branches;
            public IEnumerable<string> Errors;

            private Params() {}
            public int Limit { get; private set; }
            public bool UseConsole { get; private set; }
            public bool UseCapsLock { get; private set; }
            public bool SkipEmptyCommits { get; private set; }
            public bool IgnoreLabels { get; private set; }

            public static Params Parse(string[] args, IEnumerable<string> gitBranches)
            {
                var errors = new List<string>();
                var branches = new List<string>();

                var p = new Params();
                foreach (var o in args)
                {
                    if (o.Equals("--console-output"))
                        p.UseConsole = true;
                    else if (o.Equals("--caps-lock"))
                        p.UseCapsLock = true;
                    else if (o.Equals("--skip-empty-commits"))
                        p.SkipEmptyCommits = true;
                    else if (o.Equals("--ignore-labels"))
                        p.IgnoreLabels = true;
                    else if (o.Equals("--help"))
                    {
                        errors.Add("Usage: vault2git [options]");
                        errors.Add("options:");
                        errors.Add("   --help                  This screen");
                        errors.Add("   --console-output        Use console output (default=no output)");
                        errors.Add("   --caps-lock             Use caps lock to stop at the end of the cycle with proper finalizers (default=no caps-lock)");
                        errors.Add("   --branch=<branch>       Process only one branch from config. Branch name should be in git terms. Default=all branches from config");
                        errors.Add("   --limit=<n>             Max number of versions to take from Vault for each branch");
                        errors.Add("   --skip-empty-commits    Do not create empty commits in Git");
                        errors.Add("   --ignore-labels         Do not create Git tags from Vault labels");
                    }
                    else if (o.StartsWith(LIMIT_PARAM))
                    {
                        var l = o.Substring(LIMIT_PARAM.Length);
                        int max;
                        if (int.TryParse(l, out max))
                            p.Limit = max;
                        else
                            errors.Add(string.Format("Incorrect limit ({0}). Use integer.", l));
                    }
                    else if (o.StartsWith(BRANCH_PARAM))
                    {
                        var b = o.Substring(LIMIT_PARAM.Length);
                        if (gitBranches.Contains(b))
                            branches.Add(b);
                        else
                            errors.Add(string.Format("Unknown branch {0}. Use one specified in .config", b));
                    }
                    else
                        errors.Add(string.Format("Unknown option {0}", o));
                }
                p.Branches = !branches.Any()
                    ? gitBranches
                    : branches;
                p.Errors = errors;
                return p;
            }
        }
    }
}