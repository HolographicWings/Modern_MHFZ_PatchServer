using Modern_MHFZ_PatchServer.commands;
using Modern_MHFZ_PatchServer.logger;
using Modern_MHFZ_PatchServer.utils;
using PatchServer;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Modern_MHFZ_PatchServer.webserver
{
    internal class AdminPanel
    {
        // Handles incoming HTTP requests to the admin panel endpoint.
        public static async Task<HttpResponseData> HandleAdminAsync(HttpListenerRequest request)
        {
            HttpResponseData response = new HttpResponseData
            {
                Code = 404,
                Type = "text/plain; charset=utf-8",
                Data = Array.Empty<byte>(),
                AllowPost = true
            };

            // Check if admin panel is enabled
            if (!Config.options.WebServer.AdminPanel.Enabled)
            {
                response.Code = 200;
                response.Data = Encoding.UTF8.GetBytes("Admin Panel disabled.");
                return response;
            }

            // Check authentication
            if (!IsAdminAuthorized(request))
            {
                response.Code = 401;
                response.AddHeader("WWW-Authenticate", "Basic realm=\"MHFZ Patch Server Admin\", charset=\"UTF-8\"");

                byte[] body = Encoding.UTF8.GetBytes("Authentication required.");

                response.Data = body;

                return response;
            }
            
            response.Code = 200;
            response.Type = "text/html; charset=utf-8";
            response.AddHeader("Allow", "GET, POST");

            // Send admin panel page for GET requests
            if (string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                response.Data = getAdminPage();
                return response;
            }

            // Handle POST request for executing admin commands
            if (string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                string command = await ReadFormValueAsync(request, "command");
                string result = await ExecuteAdminCommand(command);

                response.Data = getAdminPage(result);
                return response;
            }

            return response;
        }
        // Loads the admin panel HTML page, replacing the {{RESULT}} placeholder with the provided result string.
        private static byte[] getAdminPage(string result = "")
        {
            string pageContent = string.Empty;
            if (File.Exists(Environment.CurrentDirectory + "/pages/AdminPanel.html"))
            {
                pageContent = File.ReadAllText(Environment.CurrentDirectory + "/pages/AdminPanel.html");
            }
            else
            {
                pageContent = Encoding.UTF8.GetString(ResourcesLoader.LoadResource("AdminPanel.html"));
            }

            pageContent = pageContent.Replace("{{RESULT}}", WebUtility.HtmlEncode(result));

            return Encoding.UTF8.GetBytes(pageContent);
        }
        // Validates the "Authorization" header of the incoming HTTP request. It uses a fixed-time comparison to prevent timing attacks.
        private static bool IsAdminAuthorized(HttpListenerRequest request)
        {
            string? header = request.Headers["Authorization"];

            if (string.IsNullOrWhiteSpace(header))
                return false;

            if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                return false;

            string encoded = header["Basic ".Length..].Trim();

            string decoded;

            try
            {
                byte[] bytes = Convert.FromBase64String(encoded);
                decoded = Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return false;
            }
            // The decoded string should be in the format "username:password"
            int separatorIndex = decoded.IndexOf(':');

            // If there is no colon, the format is invalid
            if (separatorIndex < 0)
                return false;

            // Split the decoded string into username and password
            string username = decoded[..separatorIndex];
            string password = decoded[(separatorIndex + 1)..];

            // Use fixed-time comparison to prevent timing attacks
            return FixedTimeEquals(username, Config.options.WebServer.AdminPanel.Username)
                && FixedTimeEquals(password, Config.options.WebServer.AdminPanel.Password);
        }
        // Reads the value of the form field from the body of a POST request.
        private static async Task<string> ReadFormValueAsync(HttpListenerRequest request, string key)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);

            string body = await reader.ReadToEndAsync();

            // The body is expected to be in the format "key1=value1&key2=value2".
            foreach (string part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = part.Split('=', 2);

                if (pair.Length != 2)
                    continue;

                string name = WebUtility.UrlDecode(pair[0]) ?? "";
                string value = WebUtility.UrlDecode(pair[1]) ?? "";

                if (name == key)
                    return value;
            }

            return "";
        }
        // Executes the given admin command using the CommandDispatcher and returns the result as a string.
        private static async Task<string> ExecuteAdminCommand(string command)
        {
            Logger.LogDebug($"Executing admin command: {command}", "WebServer");
            return await CommandDispatcher.SendCommand(command, CommandDispatcher.CommandSource.Web);
        }
        // Compares two strings in a fixed-time manner to prevent timing attacks. It converts the strings to byte arrays and uses CryptographicOperations.FixedTimeEquals for the comparison.
        private static bool FixedTimeEquals(string a, string b)
        {
            byte[] aBytes = Encoding.UTF8.GetBytes(a);
            byte[] bBytes = Encoding.UTF8.GetBytes(b);

            if (aBytes.Length != bBytes.Length)
                return false;

            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }
    }
}
