using Modern_MHFZ_PatchServer.logger;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using static Modern_MHFZ_PatchServer.utils.Config;

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
            Config.options.GameData.packagesManifest = JsonSerializer.Serialize(new
            {
                GamePackages = Config.options.GameData.GamePackages.Where(p => p.Enabled && p.PackageVersions.Any(v => v.Enabled)).Select(p => new
                {
                    p.Name,
                    p.Description,
                    p.LatestVersion,
                    p.Enabled,
                    p.Mandatory,
                    p.Multiple,

                    PackageVersions = p.PackageVersions.Where(v => v.Enabled).Select(v => new
                    {
                        v.Name,
                        v.Folder,
                        v.Enabled
                    }).ToArray()
                }).ToArray()
            });
        }
    }
}
