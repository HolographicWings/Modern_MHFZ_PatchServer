using Modern_MHFZ_PatchServer.logger;
using Modern_MHFZ_PatchServer.utils;
using PatchServer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Modern_MHFZ_PatchServer.webserver
{
    internal sealed class HttpResponseData
    {
        public int Code { get; set; } = 404;
        public string Type { get; set; } = "text/plain; charset=utf-8";
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string FilePath { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool AllowPost { get; set; }

        public void AddHeader(string key, string value)
        {
            Headers[key] = value;
        }
    }
    internal class WebServer
    {
        // Buffer size for file streaming.
        private static readonly int BufferSize = MathUtils.ParseSizeToBytesInt("64K");
        public static async Task RunAsync(CancellationToken cancellationToken)
        {
            Logger.LogInfo("Starting web server...", "WebServer");
            using var listener = new HttpListener();
            // Interfaces to listen on.
            listener.Prefixes.Add($"http://{Config.options.WebServer.Listener}:{Config.options.WebServer.Port}/");

            listener.Start();

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    listener.Stop();
                }
                catch
                {
                    // Already stopped.
                }
            });

            // Client limit, 0 = unlimited.
            SemaphoreSlim? clientLimiter = Config.options.WebServer.MaxClients > 0 ? new SemaphoreSlim(Config.options.WebServer.MaxClients, Config.options.WebServer.MaxClients) : null;

            Logger.LogInfo($"Web server running on http://{Config.options.WebServer.Listener}:{Config.options.WebServer.Port}/", "WebServer");
            
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    HttpListenerContext context;

                    try
                    {
                        context = await listener.GetContextAsync();
                    }
                    catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    _ = Task.Run(async () =>
                    {
                        // If we have a client limit, try to enter the semaphore. If we can't, return 503.
                        if (clientLimiter is null)
                        {
                            await HandleRequestAsync(context);
                            return;
                        }

                        bool accepted = await clientLimiter.WaitAsync(0);

                        // Reached the client limit.
                        if (!accepted)
                        {
                            await SendTooManyClientsAsync(context);
                            return;
                        }

                        // Handle the request, and ensure we release the semaphore when done.
                        try
                        {
                            await HandleRequestAsync(context);
                        }
                        finally
                        {
                            clientLimiter.Release();
                        }
                    });
                }
            }
            finally
            {
                listener.Stop();
                Logger.LogInfo("Web server stopped.", "WebServer");
            }

        }
        // Main request handling method. Determines the response based on the request path.
        private static async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                // Normalize the path to ensure consistent handling.
                string path = NormalizeHttpPath(context.Request.Url?.AbsolutePath ?? "/");

                HttpResponseData response = path switch
                {
                    "/" => GetStringStream("Hello from console server."), // Root path, can be used for health checks.
                    "/check" => GetManifestStream(context.Request,Config.options.GameData.manifest), // Legacy Manifest for base package, with hash and file paths. (Legacy launchers only)
                    "/check2" => GetManifestStream(context.Request,Config.options.GameData.manifest2), // New Manifest for base package, with hash, file paths and file sizes, used by new generation launchers. (New launchers only)
                    "/ButterVersion.txt" => GetStringStream(Config.options.GameData.BasePackageCurrentVersion), // Send the current version of the base package. (Legacy and New launchers)
                    string p when IsPathOrChild(p, "/packages") => await GetPackageFileStream(context.Request, path), // Holding files and manifests of other packages. (New launchers only)
                    string p when IsPathOrChild(p, "/files") => await GetRawFileStream(path), // Holding raw files that are not part of any package, like launcher assets. (Legacy and New launchers)
                    "/status" => GetStringStream("Not implemented yet..."), // Can be used to provide server status or metrics. (New launchers only)
                    "/admin" => await AdminPanel.HandleAdminAsync(context.Request), // Admin panel for sending commands. (Protected, requires admin authentication)
                    _ => await GetBaseFileStream(path) // Default handler, serves files of the base package. (Legacy and New launchers)
                };

                // If the response does not allow POST and the request method is not GET, return 405 Method Not Allowed.
                if (!response.AllowPost && !string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 405;
                    context.Response.Headers["Allow"] = "GET";
                    return;
                }

                // Write the response to the client.
                await WriteResponseAsync(context.Request, context.Response, response);
            }
            // Handle client disconnects gracefully, which can happen if the client cancels the download or loses connection.
            catch (Exception ex) when (IsClientDisconnect(ex))
            {
                Logger.LogDebug("Client disconnected before download completed.", "WebServer");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Exception occurred: {ex.Message}", "WebServer");
                try
                {
                    context.Response.StatusCode = 500;
                }
                catch
                {
                    Logger.LogError($"Exception occurred while handling response: {ex.Message}", "WebServer");
                    // Response possibly already closed or partially sent.
                }
            }
            // Ensure the response is closed to free resources.
            finally
            {
                context.Response.Close();
            }
        }
        // Normalizes the HTTP path by replacing backslashes with forward slashes, collapsing multiple slashes into one, and ensuring it starts with a slash.
        private static string NormalizeHttpPath(string path)
        {
            path = (path ?? "/").Replace('\\', '/');

            while (path.Contains("//", StringComparison.Ordinal))
                path = path.Replace("//", "/", StringComparison.Ordinal);

            return string.IsNullOrWhiteSpace(path) ? "/" : path;
        }
        // Writes the HTTP response based on the provided HttpResponseData, handling file streaming if necessary.
        private static async Task WriteResponseAsync(HttpListenerRequest request, HttpListenerResponse httpResponse, HttpResponseData response)
        {
            httpResponse.StatusCode = response.Code;
            httpResponse.ContentType = response.Type;

            // Add security headers to the response to mitigate common web vulnerabilities.
            AddSecurityHeaders(request, httpResponse, response);

            // Add headers specified in the response data.
            foreach (var header in response.Headers)
            {
                httpResponse.Headers[header.Key] = header.Value;
            }

            // If a file path is specified, stream the file. Otherwise, write the data from memory.
            if (!string.IsNullOrWhiteSpace(response.FilePath))
            {
                // Hold case of file not existing.
                if (!File.Exists(response.FilePath))
                {
                    httpResponse.StatusCode = 404;

                    byte[] notFound = Encoding.UTF8.GetBytes("Not found");

                    httpResponse.ContentType = "text/plain; charset=utf-8";
                    httpResponse.ContentLength64 = notFound.Length;

                    await httpResponse.OutputStream.WriteAsync(notFound);
                    await httpResponse.OutputStream.FlushAsync();
                    return;
                }

                FileInfo info = new FileInfo(response.FilePath);

                httpResponse.SendChunked = false; // Set content length and disable chunked encoding to allow the client to show accurate progress and speed.
                httpResponse.ContentLength64 = info.Length;

                Logger.LogDebug($"Sending file: {response.FilePath} ({info.Length} bytes)", "WebServer");

                // Stream the file with throttling to respect the bandwidth limit, 0 = unlimited.
                await SendFileThrottledAsync(response.FilePath, httpResponse.OutputStream, MathUtils.ParseSizeToBytes(Config.options.WebServer.BandwidthLimit));

                return;
            }

            httpResponse.SendChunked = false; // Set content length and disable chunked encoding to allow the client to show accurate progress and speed.
            httpResponse.ContentLength64 = response.Data.LongLength;

            if (response.Data.Length > 0)
            {
                // Write the response to the output stream.
                await httpResponse.OutputStream.WriteAsync(response.Data);
                await httpResponse.OutputStream.FlushAsync();
            }
        }
        // Streams the specified file to the output stream while applying bandwidth limit.
        private static async Task SendFileThrottledAsync(string filePath, Stream output, long maxBytesPerSecond)
        {
            // Buffer for reading the file in chunks.
            byte[] buffer = new byte[BufferSize];

            // Open the file stream with asynchronous and sequential scan options for optimal performance.
            await using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: BufferSize, options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            long totalSent = 0;
            var stopwatch = Stopwatch.StartNew(); // Stopwatch to track elapsed time for bandwidth throttling.

            while (true)
            {
                // Read a chunk of the file.
                int read = await fileStream.ReadAsync(buffer.AsMemory(0, buffer.Length));

                if (read == 0)
                    break;

                // Write the chunk to the output stream.
                await output.WriteAsync(buffer.AsMemory(0, read));

                totalSent += read;

                // Apply bandwidth throttling if a limit is set.
                if (maxBytesPerSecond > 0)
                    await ApplyRateLimitAsync(totalSent, stopwatch, maxBytesPerSecond);
            }

            // Ensure all data is flushed to the client.
            await output.FlushAsync();

            Logger.LogDebug($"Finished sending {totalSent} bytes from {filePath}", "WebServer");
        }
        // Applies bandwidth throttling by calculating the expected time to send the data based on the specified limit and delaying if the actual time is less than expected.
        private static async Task ApplyRateLimitAsync(long totalBytesSent, Stopwatch stopwatch, long maxBytesPerSecond)
        {
            double expectedMs = totalBytesSent * 1000.0 / maxBytesPerSecond;
            double actualMs = stopwatch.Elapsed.TotalMilliseconds;

            double delayMs = expectedMs - actualMs;

            if (delayMs > 1)
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs));
        }
        // Handles requests for files in the base package.
        private static Task<HttpResponseData> GetBaseFileStream(string path)
        {
            if (Config.options.GameData.fileHashes.TryGetValue(path, out var file) && File.Exists(file.path))
            {
                return Task.FromResult(new HttpResponseData
                {
                    Code = 200,
                    Type = "application/octet-stream",
                    FilePath = file.path
                });
            }

            // File not found in the base package.
            return Task.FromResult(new HttpResponseData
            {
                Code = 404,
                Type = "application/octet-stream",
                Data = Array.Empty<byte>()
            });
        }
        // Handles requests for files in another package.
        private static Task<HttpResponseData> GetPackageFileStream(HttpListenerRequest request, string path)
        {
            // Extract the relative path after "/packages/".
            string relative = path["/packages".Length..].Trim('/');

            // Split the path into components: packageName/versionName/filePath
            string[] p = relative.Split('/', 3, StringSplitOptions.RemoveEmptyEntries);

            // Default response for not found or invalid requests.
            HttpResponseData response = new HttpResponseData
            {
                Code = 404,
                Type = "application/octet-stream",
                Data = Array.Empty<byte>()
            };

            // "/packages"
            // Send the list of all packages.
            if (p.Length == 0)
            {
                return Task.FromResult(GetStringStream(Config.options.GameData.packagesManifest));
            }

            // "/packages/{packageName}/{versionName}"
            // Handle a defined package and version.
            if (p.Length == 3)
            {
                var packageName = p[0];
                var versionName = p[1];
                var fileName = '/' + p[2];

                var gamePackage = Config.options.GameData.GamePackages.FirstOrDefault(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));

                // Check if the package exists and is enabled.
                if (gamePackage != null)
                {
                    if (!gamePackage.Enabled)
                    {
                        response.Code = 404;
                        return Task.FromResult(response);
                    }

                    // "/packages/lastest/{versionName}"
                    // Allow "latest" as a version name to refer to the current version of the package.
                    if (versionName == "latest")
                    {
                        versionName = gamePackage.CurrentVersion;
                    }

                    var packageVersion = gamePackage.PackageVersions.FirstOrDefault(v => v.Name.Equals(versionName, StringComparison.OrdinalIgnoreCase));

                    // Check if the version exists and is enabled.
                    if (packageVersion != null)
                    {
                        if (!packageVersion.Enabled)
                        {
                            response.Code = 404;
                            return Task.FromResult(response);
                        }

                        // "/packages/{packageName}/{versionName}/check"
                        // Send the legacy manifest for this package version.
                        if (fileName == "/check")
                        {
                            return Task.FromResult(GetManifestStream(request, packageVersion.manifest));
                        }
                        // "/packages/{packageName}/{versionName}/check2"
                        // Send the new manifest for this package version.
                        if (fileName == "/check2")
                        {
                            return Task.FromResult(GetManifestStream(request, packageVersion.manifest2));
                        }
                        // "/packages/{packageName}/{versionName}/ButterVersion.txt"
                        // Send the version name of this package version.
                        else if (fileName == "/ButterVersion.txt")
                        {
                            return Task.FromResult(GetStringStream(packageVersion.Name));
                        }
                        // "/packages/{packageName}/{versionName}/{file}"
                        // Send the specified file from this package version.
                        else if (packageVersion.fileHashes.TryGetValue(fileName, out var descriptor) && File.Exists(descriptor.path))
                        {
                            FileInfo info = new FileInfo(descriptor.path);

                            response.Code = 200;
                            response.Type = "application/octet-stream";
                            response.FilePath = descriptor.path;
                        }
                    }
                }
            }

            return Task.FromResult(response);
        }
        // Handles requests for raw files that are not part of any package.
        private static Task<HttpResponseData> GetRawFileStream(string path)
        {
            // Extract the relative path after "/files/".
            string p = "/" + path["/files/".Length..].TrimStart('/');

            if (Config.options.GameData.RawFilesList.TryGetValue(p, out var fPath) && File.Exists(fPath))
            {
                return Task.FromResult(new HttpResponseData
                {
                    Code = 200,
                    Type = "application/octet-stream",
                    Data = Array.Empty<byte>(),
                    FilePath = fPath
                });
            }

            // File not found in the raw files.
            return Task.FromResult(new HttpResponseData
            {
                Code = 404,
                Type = "application/octet-stream",
                Data = Array.Empty<byte>()
            });
        }
        // Sends a 503 Service Unavailable response when the server is overloaded with clients.
        private static async Task SendTooManyClientsAsync(HttpListenerContext context)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes("Too many clients. Try again later.");

                context.Response.StatusCode = 503;
                context.Response.ContentType = "text/plain; charset=utf-8";
                context.Response.Headers["Retry-After"] = "5"; // Suggest the client to retry after 5 seconds.
                context.Response.ContentLength64 = body.Length;

                await context.Response.OutputStream.WriteAsync(body);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Exception while sending 503: {ex.Message}", "WebServer");
            }
            finally
            {
                context.Response.Close();
            }
        }
        // Determines the cause of the disconnection exception.
        private static bool IsClientDisconnect(Exception ex)
        {
            if (ex is IOException)
                return true;

            if (ex is ObjectDisposedException)
                return true;

            if (ex is HttpListenerException hle)
            {
                return hle.ErrorCode is
                    64 or // ERROR_NETNAME_DELETED
                    995 or // ERROR_OPERATION_ABORTED
                    1229 or // ERROR_CONNECTION_INVALID
                    1236;   // ERROR_CONNECTION_ABORTED
            }

            return ex.InnerException is not null && IsClientDisconnect(ex.InnerException);
        }
        // Adds security headers to the HTTP response to mitigate common web vulnerabilities.
        private static void AddSecurityHeaders(HttpListenerRequest request, HttpListenerResponse response, HttpResponseData appResponse)
        {
            // No MIME sniffing.
            response.Headers["X-Content-Type-Options"] = "nosniff";

            // Referrer policy - do not send referrer information.
            response.Headers["Referrer-Policy"] = "no-referrer";
            // Clickjacking protection - disallow framing by default.
            response.Headers["X-Frame-Options"] = "DENY";

            bool isHtml = appResponse.Type.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);

            // CSP (Content Security Policy) - useless for non-HTML content.
            if (isHtml)
            {
                // Very restrictive by default, allowing only inline styles (for admin panel).
                response.Headers["Content-Security-Policy"] =
                    "default-src 'none'; " + // Disallow everything by default.
                    "script-src 'none'; " + // No scripts allowed.
                    "style-src 'unsafe-inline'; " + // Allow inline styles.
                    "img-src 'none'; " + // No images allowed.
                    "font-src 'none'; " + // No fonts allowed.
                    "connect-src 'none'; " + // No AJAX/websocket connections allowed.
                    "media-src 'none'; " + // No audio/video allowed.
                    "object-src 'none'; " + // No plugins allowed.
                    "frame-src 'none'; " + // No frames allowed.
                    "base-uri 'none'; " + // No base URI allowed.
                    "form-action 'self'; " + // Allow forms to the same origin.
                    "frame-ancestors 'none';"; // No framing allowed.
            }

            // Only in HTTPS. On HTTP, this header is useless.
            if (request.IsSecureConnection)
            {
                // HSTS - tell browsers to always use HTTPS for this domain for the next year.
                response.Headers["Strict-Transport-Security"] = "max-age=31536000";
            }
        }

        // Handles requests for the manifest, supporting ETag-based caching to allow clients to avoid re-downloading the manifest if it hasn't changed.
        private static HttpResponseData GetManifestStream(HttpListenerRequest request, string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            string etag = BuildManifestETag(data); // Generate an ETag based on the content of the manifest.

            string? clientETag = request.Headers["If-None-Match"]; // Get the ETag sent by the client to check if their cached version is still valid.

            // If the client's ETag matches the server's ETag, return a 304 Not Modified response without sending the manifest data again.
            if (EtagMatches(clientETag, etag))
            {
                var notModified = new HttpResponseData
                {
                    Code = 304,
                    Type = "text/plain; charset=utf-8",
                    Data = Array.Empty<byte>()
                };

                notModified.AddHeader("ETag", etag);
                return notModified;
            }

            // If the ETag does not match, return the manifest data with the ETag header for future caching.
            var response = new HttpResponseData
            {
                Code = 200,
                Type = "text/plain; charset=utf-8",
                Data = data
            };

            response.AddHeader("ETag", etag);
            return response;
        }

        // Compares the client's ETag with the server's ETag to determine if they match.
        private static bool EtagMatches(string? clientETag, string serverETag)
        {
            if (string.IsNullOrWhiteSpace(clientETag))
                return false;

            string c = clientETag.Trim();
            string s = serverETag.Trim();

            return string.Equals(c, s, StringComparison.Ordinal) || string.Equals(c.Trim('\"'), s.Trim('\"'), StringComparison.Ordinal);
        }

        // Builds an ETag for the manifest by hashing it.
        private static string BuildManifestETag(byte[] data)
        {
            byte[] hash = SHA256.HashData(data);
            return '\"' + Convert.ToHexString(hash).ToLowerInvariant() + '\"';
        }
        private static HttpResponseData GetStringStream(string text)
        {
            return new HttpResponseData
            {
                Code = 200,
                Type = "text/plain; charset=utf-8",
                Data = Encoding.UTF8.GetBytes(text),
            };
        }
        // Determines if the given path is equal to the root or is a child of the root (i.e., starts with the root followed by a slash).
        private static bool IsPathOrChild(string path, string root)
        {
            return path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
