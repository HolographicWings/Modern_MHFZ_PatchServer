using Modern_MHFZ_PatchServer.logger;
using PatchServer;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Text.Json.Serialization;

namespace Modern_MHFZ_PatchServer.utils
{
    [JsonSerializable(typeof(Config.RootConfig))]
    internal partial class ConfigJsonContext : JsonSerializerContext
    {
    }
    public class Config
    {
        // Json Context
        public sealed class RootConfig
        {
            public GameDataOptions GameData { get; init; } = new GameDataOptions();
            public WebServerOptions WebServer { get; init; } = new WebServerOptions();
            public LoggerOptions Logger { get; init; } = new LoggerOptions();
        }
        public sealed class GameDataOptions
        {
            public string RootFolder { get; set; } = string.Empty;
            public string BasePackageVersion { get; set; } = "v1.0";
            public int ChecksumThreads { get; set; } = 4;
            public bool ChecksumCache { get; set; } = true;
            public GamePackage[] GamePackages { get; init; } = Array.Empty<GamePackage>();
            [JsonIgnore] public string[] FileList { get; set; } = Array.Empty<string>();
            [JsonIgnore] public ConcurrentDictionary<string, string> RawFilesList { get; set; } = new ConcurrentDictionary<string, string>();
            [JsonIgnore] public ConcurrentDictionary<string, fileDescriptor> fileHashes { get; set; } = new ConcurrentDictionary<string, fileDescriptor>();
            [JsonIgnore] public string manifest { get; set; } = string.Empty;
            [JsonIgnore] public string manifest2 { get; set; } = string.Empty;
            [JsonIgnore] public string packagesManifest { get; set; } = string.Empty;
        }
        public sealed class GamePackage
        {
            public string Name { get; init; } = string.Empty;
            public bool Mandatory { get; set; } = false;
            public string Description { get; init; } = string.Empty;
            public string CurrentVersion { get; init; } = string.Empty;
            public bool Enabled { get; set; } = true;
            public GamePackageVersion[] PackageVersions { get; init; } = Array.Empty<GamePackageVersion>();
        }
        public sealed class GamePackageVersion
        {
            public string Name { get; init; } = string.Empty;
            public string Folder { get; init; } = string.Empty;
            public bool Enabled { get; set; } = true;
            [JsonIgnore] public string[] FileList { get; set; } = Array.Empty<string>();
            [JsonIgnore] public ConcurrentDictionary<string, fileDescriptor> fileHashes{ get; set; } = new ConcurrentDictionary<string, fileDescriptor>();
            [JsonIgnore] public string manifest{ get; set; } = string.Empty;
            [JsonIgnore] public string manifest2{ get; set; } = string.Empty;
        }
        public sealed class WebServerOptions
        {
            public bool Enabled { get; set; } = true;
            public int Port { get; init; } = 8094;
            public int MaxClients { get; set; } = 5;
            public string BandwidthLimit { get; set; } = "20M";
            public string Listener { get; set; } = "127.0.0.1";

            public AdminPanelOptions AdminPanel { get; set; } = new AdminPanelOptions();
        }
        public sealed class AdminPanelOptions
        {
            public bool Enabled { get; set; } = true;
            public string Username { get; set; } = "admin";
            public string Password { get; init; } = "change-me";
        }
        public sealed class LoggerOptions
        {
            public bool WriteLog { get; set; } = true;
            public bool Debug { get; set; } = false;
            public bool PuTTYMode { get; set; } = false;
        }
        // Config instance
        public static RootConfig options { get; private set; } = new RootConfig();
        // Initialization and validation
        public static void Init()
        {
            string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            try
            {
                // Create default Json if it doesn't exist
                if (!File.Exists(configPath))
                {
                    Logger.LogWarning("config.json not found, creating default config.json", "Config");
                    string defaultJson = System.Text.Json.JsonSerializer.Serialize(new RootConfig(), ConfigJsonContext.Default.RootConfig);
                    File.WriteAllText(configPath, defaultJson);
                }

                string json = File.ReadAllText(configPath);

                // Deserialize with source-generated context
                options = System.Text.Json.JsonSerializer.Deserialize(json, ConfigJsonContext.Default.RootConfig) ?? throw new InvalidOperationException("config.json is empty or invalid.");

                Logger.LogInfo("Checking configuration...", "Config");
                Validate();
            }
            catch (WarningException ex)
            {
                Logger.LogWarning($"Warning initializing config: {ex.Message}", "Config");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error initializing config: {ex.Message}", "Config");
                throw;
            }
        }
        // Validates the configuration and throws exceptions if invalid
        public static void Validate()
        {
            // GameDate
            if (options.GameData is null)
                throw new InvalidOperationException("GameData config section is missing.");

            if (options.GameData.RootFolder.Length == 0)
                options.GameData.RootFolder = Path.Combine(Environment.CurrentDirectory, "Game");

            if (string.IsNullOrWhiteSpace(options.GameData.BasePackageVersion))
                throw new InvalidOperationException("BasePackageVersion is required.");

            if(options.GameData.ChecksumThreads > Environment.ProcessorCount)
                Logger.LogWarning($"ChecksumThreads is above the number of processor cores, clamping value to {Environment.ProcessorCount}.", "Config");
            options.GameData.ChecksumThreads = Math.Clamp(options.GameData.ChecksumThreads, 1, Environment.ProcessorCount);

            // GamePackages
            var packageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in options.GameData.GamePackages)
            {
                if (string.IsNullOrWhiteSpace(package.Name))
                    throw new InvalidOperationException("Package Name is required.");

                if (package.Name.ToLower() == "base")
                    throw new InvalidOperationException("Package Name cannot be 'Base'.");

                // Check for duplicate package names
                if (!packageNames.Add(package.Name))
                    throw new InvalidOperationException($"Duplicate package name: {package.Name}");

                var versionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var versionFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // PackageVersions
                for (int i = 0; i < package.PackageVersions.Length; i++)
                {
                    var version = package.PackageVersions[i];

                    if (string.IsNullOrWhiteSpace(version.Name))
                        throw new InvalidOperationException("Package Version Name is required.");

                    if (string.IsNullOrWhiteSpace(version.Folder))
                        throw new InvalidOperationException("Package Version Folder is required.");

                    if (version.Name == "latest")
                        throw new InvalidOperationException("Package Version Name cannot be 'latest'.");

                    // Check for duplicate version names and folders within the same package
                    if (!versionNames.Add(version.Name))
                        throw new InvalidOperationException(
                            $"Duplicate version name '{version.Name}' in package '{package.Name}'.");

                    if (!versionFolders.Add(version.Folder))
                        throw new InvalidOperationException(
                            $"Duplicate version folder '{version.Folder}' in package '{package.Name}'.");
                }
            }

            // WebServer
            if (options.WebServer is null)
                throw new InvalidOperationException("WebServer config section is missing.");

            if (options.WebServer.AdminPanel is null)
                throw new InvalidOperationException("AdminPanel config section is missing.");

            if (!IPAddress.TryParse(options.WebServer.Listener, out _) && options.WebServer.Listener != "localhost" && options.WebServer.Listener != "*" && options.WebServer.Listener != "+")
                throw new InvalidOperationException("Listener must be a valid IP address.");

            if (options.WebServer.Port is < 1 or > 65535)
                throw new InvalidOperationException("PatchPort must be between 1 and 65535.");

            if (options.WebServer.MaxClients < 0)
                options.WebServer.MaxClients = 0;

            if (string.IsNullOrWhiteSpace(options.WebServer.BandwidthLimit))
                options.WebServer.BandwidthLimit = "20M";

            // AdminPanel
            if (options.WebServer.AdminPanel.Enabled)
            {
                if (string.IsNullOrWhiteSpace(options.WebServer.AdminPanel.Username))
                    throw new InvalidOperationException("AdminPanel Username is required when AdminPanel is enabled.");

                if (string.IsNullOrWhiteSpace(options.WebServer.AdminPanel.Password))
                    throw new InvalidOperationException("AdminPanel Password must be changed and can't be empty when AdminPanel is enabled.");

                if (options.WebServer.AdminPanel.Password == "change-me")
                    Logger.LogWarning("AdminPanel Password should be changed from the default value.", "Config");
            }
        }
    }
}