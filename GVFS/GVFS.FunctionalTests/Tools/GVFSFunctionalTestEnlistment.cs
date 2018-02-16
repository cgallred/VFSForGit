﻿using GVFS.FunctionalTests.FileSystemRunners;
using GVFS.FunctionalTests.Should;
using GVFS.FunctionalTests.Tests;
using GVFS.Tests.Should;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GVFS.FunctionalTests.Tools
{
    public class GVFSFunctionalTestEnlistment
    {
        private const string ZeroBackgroundOperations = "Background operations: 0\r\n";
        private const string LockHeldByGit = "GVFS Lock: Held by {0}";
        private const int SleepMSWaitingForStatusCheck = 100;
        private const int DefaultMaxWaitMSForStatusCheck = 5000;

        private GVFSProcess gvfsProcess;
        
        private GVFSFunctionalTestEnlistment(string pathToGVFS, string enlistmentRoot, string repoUrl, string commitish, string localCacheRoot = null)
        {
            this.EnlistmentRoot = enlistmentRoot;
            this.RepoUrl = repoUrl;
            this.Commitish = commitish;
            
            if (localCacheRoot == null)
            {
                if (GVFSTestConfig.NoSharedCache)
                {
                    // eg C:\Repos\GVFSFunctionalTests\enlistment\7942ca69d7454acbb45ea39ef5be1d15\.gvfs\.gvfsCache
                    localCacheRoot = GetRepoSpecificLocalCacheRoot(enlistmentRoot);
                }
                else
                {
                    // eg C:\Repos\GVFSFunctionalTests\.gvfsCache
                    // Ensures the general cache is not cleaned up between test runs
                    localCacheRoot = Path.Combine(Properties.Settings.Default.EnlistmentRoot, "..", ".gvfsCache");                    
                }
            }

            this.LocalCacheRoot = localCacheRoot;
            this.gvfsProcess = new GVFSProcess(pathToGVFS, this.EnlistmentRoot, this.LocalCacheRoot);
        }
        
        public string EnlistmentRoot
        {
            get; private set;
        }

        public string RepoUrl
        {
            get; private set;
        }
        
        public string LocalCacheRoot { get; }

        public string RepoRoot
        {
            get { return Path.Combine(this.EnlistmentRoot, "src"); }
        }

        public string DotGVFSRoot
        {
            get { return Path.Combine(this.EnlistmentRoot, ".gvfs"); }
        }

        public string GVFSLogsRoot
        {
            get { return Path.Combine(this.DotGVFSRoot, "logs"); }
        }

        public string DiagnosticsRoot
        {
            get { return Path.Combine(this.DotGVFSRoot, "diagnostics"); }
        }

        public string Commitish
        {
            get; private set;
        }

        public static GVFSFunctionalTestEnlistment CloneAndMountWithPerRepoCache(string pathToGvfs, string commitish = null)
        {
            string enlistmentRoot = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();
            string localCache = GVFSFunctionalTestEnlistment.GetRepoSpecificLocalCacheRoot(enlistmentRoot);
            return CloneAndMount(pathToGvfs, enlistmentRoot, commitish, localCache);
        }

        public static GVFSFunctionalTestEnlistment CloneAndMount(string pathToGvfs, string commitish = null, string localCacheRoot = null)
        {
            string enlistmentRoot = GVFSFunctionalTestEnlistment.GetUniqueEnlistmentRoot();
            return CloneAndMount(pathToGvfs, enlistmentRoot, commitish, localCacheRoot);
        }

        public static string GetUniqueEnlistmentRoot()
        {
            return Path.Combine(Properties.Settings.Default.EnlistmentRoot, Guid.NewGuid().ToString("N"));
        }

        public string GetObjectRoot(FileSystemRunner fileSystem)
        {
            IEnumerable<FileSystemInfo> localCacheRootItems = this.LocalCacheRoot.ShouldBeADirectory(fileSystem).WithItems();
            localCacheRootItems.Count().ShouldEqual(2, "Expected local cache root to contain 2 items. Actual items: " + string.Join(",", localCacheRootItems));
            DirectoryInfo[] directories = localCacheRootItems.Where(info => info is DirectoryInfo).Cast<DirectoryInfo>().ToArray();
            directories.Length.ShouldEqual(1, this.LocalCacheRoot + " is expected to have only one folder. Actual: " + directories.Count());
            return Path.Combine(directories[0].FullName, "gitObjects");
        }

        public string GetPackRoot(FileSystemRunner fileSystem)
        {
            return Path.Combine(this.GetObjectRoot(fileSystem), "pack");
        }

        public void DeleteEnlistment()
        {
            TestResultsHelper.OutputGVFSLogs(this);

            // Use cmd.exe to delete the enlistment as it properly handles tombstones and reparse points
            CmdRunner.DeleteDirectoryWithRetry(this.EnlistmentRoot);
        }

        public void CloneAndMount()
        {
            this.gvfsProcess.Clone(this.RepoUrl, this.Commitish);

            this.MountGVFS();
            GitProcess.Invoke(this.RepoRoot, "checkout " + this.Commitish);
            GitProcess.Invoke(this.RepoRoot, "branch --unset-upstream");
            GitProcess.Invoke(this.RepoRoot, "config core.abbrev 40");
            GitProcess.Invoke(this.RepoRoot, "config user.name \"Functional Test User\"");
            GitProcess.Invoke(this.RepoRoot, "config user.email \"functional@test.com\"");
        }

        public void MountGVFS()
        {
            this.gvfsProcess.Mount();
        }

        public bool TryMountGVFS()
        {
            string output;
            return this.TryMountGVFS(out output);
        }

        public bool TryMountGVFS(out string output)
        {
            return this.gvfsProcess.TryMount(out output);
        }

        public string Prefetch(string args, bool failOnError = true)
        {
            return this.gvfsProcess.Prefetch(args, failOnError);
        }

        public void Repair()
        {
            this.gvfsProcess.Repair();
        }

        public string Diagnose()
        {
            return this.gvfsProcess.Diagnose();
        }

        public string Status()
        {
            return this.gvfsProcess.Status();
        }

        public bool WaitForBackgroundOperations(int maxWaitMilliseconds = DefaultMaxWaitMSForStatusCheck)
        {
            return this.WaitForStatus(maxWaitMilliseconds, ZeroBackgroundOperations);
        }

        public bool WaitForLock(string lockCommand, int maxWaitMilliseconds = DefaultMaxWaitMSForStatusCheck)
        {
            return this.WaitForStatus(maxWaitMilliseconds, string.Format(LockHeldByGit, lockCommand));
        }

        public void UnmountGVFS()
        {
            this.gvfsProcess.Unmount();
        }

        public string GetCacheServer()
        {
            return this.gvfsProcess.CacheServer("--get");
        }

        public string SetCacheServer(string arg)
        {
            return this.gvfsProcess.CacheServer("--set " + arg);
        }

        public void UnmountAndDeleteAll()
        {
            this.UnmountGVFS();
            this.DeleteEnlistment();
        }

        public string GetVirtualPathTo(string pathInRepo)
        {
            return Path.Combine(this.RepoRoot, pathInRepo);
        }

        public string GetObjectPathTo(string objectHash)
        {
            return Path.Combine(
                this.RepoRoot,
                TestConstants.DotGit.Objects.Root,
                objectHash.Substring(0, 2),
                objectHash.Substring(2));
        }
        
        private static GVFSFunctionalTestEnlistment CloneAndMount(string pathToGvfs, string enlistmentRoot, string commitish, string localCacheRoot)
        {
            GVFSFunctionalTestEnlistment enlistment = new GVFSFunctionalTestEnlistment(
                pathToGvfs,
                enlistmentRoot ?? GetUniqueEnlistmentRoot(),
                GVFSTestConfig.RepoToClone,
                commitish ?? Properties.Settings.Default.Commitish,
                localCacheRoot ?? GVFSTestConfig.LocalCacheRoot);

            try
            {
                enlistment.CloneAndMount();
            }
            catch (Exception e)
            {
                Console.WriteLine("Unhandled exception in CloneAndMount: " + e.ToString());
                TestResultsHelper.OutputGVFSLogs(enlistment);
                throw;
            }

            return enlistment;
        }

        private static string GetRepoSpecificLocalCacheRoot(string enlistmentRoot)
        {
            return Path.Combine(enlistmentRoot, ".gvfs", ".gvfsCache");
        }

        private bool WaitForStatus(int maxWaitMilliseconds, string statusShouldContain)
        {
            string status = null;
            int totalWaitMilliseconds = 0;
            while (totalWaitMilliseconds <= maxWaitMilliseconds && (status == null || !status.Contains(statusShouldContain)))
            {
                Thread.Sleep(SleepMSWaitingForStatusCheck);
                status = this.Status();
                totalWaitMilliseconds += SleepMSWaitingForStatusCheck;
            }

            return totalWaitMilliseconds <= maxWaitMilliseconds;
        }
    }
}
