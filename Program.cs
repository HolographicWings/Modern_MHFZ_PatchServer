using Modern_MHFZ_PatchServer.commands;
using Modern_MHFZ_PatchServer.logger;
using Modern_MHFZ_PatchServer.utils;
using Modern_MHFZ_PatchServer.webserver;
using System.Collections.Concurrent;

namespace PatchServer
{
    public class Program
    {
        private static CancellationTokenSource cts = new CancellationTokenSource();
        private static bool isStopping = false;
        public static async Task Main()
        {
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Empêche la fermeture brutale du process.
                cts.Cancel();

                Logger.LogInfo("Cancellation requested...", "Main");
            };

            try
            {
                Console.WriteLine("Starting patch server.");

                // Read and validate JSON config
                Config.Init();
                
                // Raw file share
                string rawFilesPath = Path.Combine(Environment.CurrentDirectory, "Files");
                Logger.LogInfo("Processing raw files at: " + rawFilesPath, "Main");

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Config.options.GameData.ChecksumThreads,
                    CancellationToken = cts.Token
                };

                if (Config.options.WebServer.Enabled)
                {
                    //Create Files folder if it doesn't exist
                    Directory.CreateDirectory(rawFilesPath);

                    // Process files in multithread
                    string[] rawFiles = Directory.GetFiles(rawFilesPath, "*", SearchOption.AllDirectories);
                    Parallel.ForEach(rawFiles, parallelOptions, file =>
                    {
                        string rPath = '/' + Path.GetRelativePath(rawFilesPath, file).Replace('\\', '/');
                        Config.options.GameData.RawFilesList.TryAdd(rPath, file);
                        Logger.LogDebug($"Added to Files: {rPath}", "Main");
                    });
                }

                // Base Package

                // Create Game folder if it doesn't exist
                Directory.CreateDirectory(Config.options.GameData.RootFolder);

                string basePackagePath = Path.Combine(Config.options.GameData.RootFolder, "Base", Config.options.GameData.BasePackageVersion);
                Logger.LogInfo("Processing base package at: " + basePackagePath, "Main");

                Directory.CreateDirectory(basePackagePath);

                Config.options.GameData.FileList = Directory.GetFiles(basePackagePath, "*", SearchOption.AllDirectories);

                // Read existing fileinfos for persistent digest if enabled
                fileDescriptor[] basePersistentInfos = Array.Empty<fileDescriptor>();
                bool basePersistentInfosEdited = false;
                if (Config.options.GameData.ChecksumCache)
                {
                    basePersistentInfos = FileManifest.readFileinfo(basePackagePath);
                }

                // Process files in multithread
                Parallel.ForEach(Config.options.GameData.FileList, parallelOptions, file =>
                {
                    parallelOptions.CancellationToken.ThrowIfCancellationRequested();

                    var info = new FileInfo(file);

                    string rPath = '/' + Path.GetRelativePath(basePackagePath, file).Replace('\\', '/');

                    // Check file blacklist
                    if (!FileRevelance.isFileRelevant(rPath))
                        return;

                    // Compute hash with persistent digest if enabled
                    string hash = string.Empty;
                    if (Config.options.GameData.ChecksumCache)
                    {
                        var existingInfo = basePersistentInfos.FirstOrDefault(f => f.path == rPath);
                        // If file unchanged, reuse hash
                        if (existingInfo.path != null && existingInfo.edit == info.LastWriteTime.Ticks && existingInfo.size == info.Length)
                        {
                            hash = existingInfo.hash;
                            Logger.LogDebug($"File unchanged: {rPath}", "Main");
                        }
                        // If file changed or new, hash and mark persistent infos as edited
                        else
                        {
                            hash = Checksum.ComputeSha256(file);
                            Logger.LogDebug($"File changed, rehashing: {rPath}", "Main");
                            basePersistentInfosEdited = true;
                        }
                    }
                    // No persistent digest, just hash
                    else
                    {
                        hash = Checksum.ComputeSha256(file);
                        Logger.LogDebug($"File hashed: {rPath}", "Main");
                    }

                    // Add to file hashes, throw if duplicate path (shouldn't happen)
                    if (!Config.options.GameData.fileHashes.TryAdd(rPath, new fileDescriptor(file, hash, info.LastWriteTime.Ticks, info.Length)))
                    {
                        throw new InvalidOperationException($"Duplicate file path detected: {rPath}");
                    }
                });

                // If persistent checksum enabled and any file was changed, rewrite fileinfos
                if (Config.options.GameData.ChecksumCache && basePersistentInfosEdited)
                {
                    File.WriteAllText(Path.Combine(basePackagePath, "fileinfos"), FileManifest.buildManifest(Config.options.GameData.fileHashes, 4));
                }

                // Build manifests for the launcher
                Config.options.GameData.manifest = FileManifest.buildManifest(Config.options.GameData.fileHashes);
                Config.options.GameData.manifest2 = FileManifest.buildManifest(Config.options.GameData.fileHashes, 3);

                // If webserver disabled, and manifest needing to be updated, write the check files for the launcher.
                if (!Config.options.WebServer.Enabled && (!Config.options.GameData.ChecksumCache || basePersistentInfosEdited ||
                !File.Exists(Path.Combine(basePackagePath, "check")) || !File.Exists(Path.Combine(basePackagePath, "check2"))))
                {
                    File.WriteAllText(Path.Combine(basePackagePath, "check"), Config.options.GameData.manifest);
                    File.WriteAllText(Path.Combine(basePackagePath, "check2"), Config.options.GameData.manifest2);
                }

                // Other Packages
                Logger.LogInfo("Processing other packages.", "Main");

                // Load packages from config, will throw if invalid
                PackagesManager.LoadPackages();

                // If webserver disabled, and manifest needing to be updated, write the packages files for the launcher.
                if (!Config.options.WebServer.Enabled && (!Config.options.GameData.ChecksumCache || basePersistentInfosEdited ||
                !File.Exists(Path.Combine(basePackagePath, "packages"))))
                {
                    File.WriteAllText(Path.Combine(basePackagePath, "packages"), Config.options.GameData.packagesManifest);
                }

                // Process each package
                foreach (var package in Config.options.GameData.GamePackages)
                {
                    if (!package.Enabled)
                        continue;

                    // Process each package versions
                    for (int i = 0; i < package.PackageVersions.Length; i++)
                    {
                        if (!package.PackageVersions[i].Enabled)
                            continue;

                        if (package.PackageVersions[i].FileList == null)
                            continue;

                        string packagePath = Path.Combine(Config.options.GameData.RootFolder, package.Name, package.PackageVersions[i].Folder);

                        // Read existing fileinfos for persistent digest if enabled
                        fileDescriptor[] persistentInfos = Array.Empty<fileDescriptor>();
                        bool persistantInfosEdited = false;
                        if (Config.options.GameData.ChecksumCache)
                        {
                            persistentInfos = FileManifest.readFileinfo(packagePath);
                        }

                        // Process files in multithread
                        Parallel.ForEach(package.PackageVersions[i].FileList, parallelOptions, file =>
                        {
                            parallelOptions.CancellationToken.ThrowIfCancellationRequested();

                            var info = new FileInfo(file);

                            string rPath = '/' + Path.GetRelativePath(packagePath, file).Replace('\\', '/');

                            // Check file blacklist
                            if (!FileRevelance.isFileRelevant(rPath))
                                return;

                            // Compute hash with persistent digest if enabled
                            string hash = string.Empty;
                            if (Config.options.GameData.ChecksumCache)
                            {
                                var existingInfo = persistentInfos.FirstOrDefault(f => f.path == rPath);
                                // If file unchanged, reuse hash
                                if (existingInfo.path != null && existingInfo.edit == info.LastWriteTime.Ticks && existingInfo.size == info.Length)
                                {
                                    hash = existingInfo.hash;
                                    Logger.LogDebug($"File unchanged: {rPath}", "Main");
                                }
                                // If file changed or new, hash and mark persistent infos as edited
                                else
                                {
                                    hash = Checksum.ComputeSha256(file);
                                    Logger.LogDebug($"File changed, rehashing: {rPath}", "Main");
                                    persistantInfosEdited = true;
                                }
                            }
                            // No persistent digest, just hash
                            else
                            {
                                hash = Checksum.ComputeSha256(file);
                                Logger.LogDebug($"File hashed: {rPath}", "Main");
                            }

                            // Add to file hashes, throw if duplicate path (shouldn't happen)
                            if (!package.PackageVersions[i].fileHashes.TryAdd(rPath, new fileDescriptor(file, hash, info.LastWriteTime.Ticks, info.Length)))
                            {
                                throw new InvalidOperationException($"Duplicate file path detected: {rPath}");
                            }

                        });

                        // If checksum cache enabled and any file was changed, rewrite fileinfos
                        if (Config.options.GameData.ChecksumCache && persistantInfosEdited)
                        {
                            File.WriteAllText(Path.Combine(packagePath, "fileinfos"), FileManifest.buildManifest(package.PackageVersions[i].fileHashes, 4));
                        }

                        // Build manifests for the launcher
                        package.PackageVersions[i].manifest = FileManifest.buildManifest(package.PackageVersions[i].fileHashes);
                        package.PackageVersions[i].manifest = FileManifest.buildManifest(package.PackageVersions[i].fileHashes, 3);

                        // If webserver disabled, and manifest needing to be updated, write the check files for the launcher.
                        if (!Config.options.WebServer.Enabled && (!Config.options.GameData.ChecksumCache || persistantInfosEdited ||
                        !File.Exists(Path.Combine(basePackagePath, "check")) || !File.Exists(Path.Combine(basePackagePath, "check2"))))
                        {
                            File.WriteAllText(Path.Combine(packagePath, "check"), package.PackageVersions[i].manifest);
                            File.WriteAllText(Path.Combine(packagePath, "check2"), package.PackageVersions[i].manifest);
                        }
                    }
                }

                Logger.LogInfo("Files initialization complete.", "Main");

                if (Config.options.WebServer.Enabled)
                {
                    // Start web server
                    Task webServerTask = WebServer.RunAsync(cts.Token);

                    if(webServerTask == null)
                    {
                        Logger.LogError("Failed to start web server.", "Main");
                        return;
                    }

                    // Start console command loop
                    while (!cts.Token.IsCancellationRequested)
                    {
                        string? input;

                        try
                        {
                            input = await Console.In.ReadLineAsync(cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        if (input is null)
                            break;

                        string result = await CommandDispatcher.SendCommand(input, CommandDispatcher.CommandSource.Console);

                        if (!string.IsNullOrWhiteSpace(result))
                            Logger.LogInfo(result, "Commands");

                        if (isStopping)
                        {
                            await Stop();
                        }
                    }

                    await webServerTask;
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning("Startup cancelled.", "Main");
            }
            catch (Exception ex)
            {
                Logger.LogError("Fatal error, server startup aborted.", "Main");
                Logger.LogError(ex.ToString(), "Main");
            }

            Logger.LogInfo("Patch server stopped.", "Main");
        }
        public static async Task RequestStop()
        {
            isStopping = true;
        }
        private static async Task Stop()
        {
            Logger.LogInfo("Stopping patch server...", "Main");
            cts.Cancel();
        }
    }
    public struct fileDescriptor
    {
        public string path { get; init; }
        public string hash { get; init; }
        public long edit { get; init; }
        public long size { get; init; }

        public fileDescriptor(string _path, string _hash, long _edit, long _size)
        {
            path = _path;
            hash = _hash;
            edit = _edit;
            size = _size;
        }
    }
}