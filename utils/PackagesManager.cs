using Modern_MHFZ_PatchServer.logger;
using System;
using System.Collections.Generic;
using System.Text;

namespace Modern_MHFZ_PatchServer.utils
{
    internal class PackagesManager
    {
        // Loads game packages based on the configuration.
        public static void LoadPackages()
        {
            foreach (var package in Config.options.GameData.GamePackages)
            {
                Logger.LogInfo($"Loading package: {package.Name} (Mandatory: {package.Mandatory})", "PackagesManager");

                for (int i = 0; i < package.PackageVersions.Length; i++)
                {
                    string packagePath = Path.Combine(Config.options.GameData.RootFolder, package.Name, package.PackageVersions[i].Folder);
                    string rPackagePath = '/' + Path.GetRelativePath(Environment.CurrentDirectory, packagePath).Replace('\\', '/');

                    Directory.CreateDirectory(packagePath);

                    Logger.LogInfo($"Package version '{package.PackageVersions[i].Name}' loaded from folder: {rPackagePath}", "PackagesManager");

                    package.PackageVersions[i].FileList = Directory.GetFiles(packagePath, "*", SearchOption.AllDirectories);
                }
            }
            ListPackages();
        }
        // Lists all enabled packages and their versions in a tab-separated format.
        public static void ListPackages()
        {
            StringBuilder sb = new StringBuilder(capacity: 1000);
            foreach (var package in Config.options.GameData.GamePackages)
            {
                if(!package.Enabled)
                    continue;

                foreach (var version in package.PackageVersions)
                {
                    if(!version.Enabled)
                        continue;

                    sb.AppendLine($"{package.Name}\t{version.Name}");
                }
            }
            Config.options.GameData.packagesManifest = sb.ToString();
        }
    }
}
