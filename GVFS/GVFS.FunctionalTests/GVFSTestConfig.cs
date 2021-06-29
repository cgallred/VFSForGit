﻿using System.IO;

namespace GVFS.FunctionalTests
{
    public static class GVFSTestConfig
    {
        public static string RepoToClone { get; set; }

        public static bool NoSharedCache { get; set; }

        public static string LocalCacheRoot { get; set; }

        public static object[] FileSystemRunners { get; set; }

        public static object[] GitRepoTestsValidateWorkTree { get; set; }

        public static bool ReplaceInboxProjFS { get; set; }

        public static string PathToGVFS
        {
            get
            {
                return Properties.Settings.Default.PathToGVFS;
            }
        }

        public static string DotGVFSRoot { get; set; }
    }
}
