using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Lenovo_Fan_Controller
{
    public static class PawnIOInstaller
    {
        private const string SetupUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
        private const string ModulesApiUrl = "https://api.github.com/repos/namazso/PawnIO.Modules/releases/latest";
        private const string DestDir = @"C:\Program Files\PawnIO";
        private const string LpcIoBin = "LpcIO.bin";

        public static async Task<bool> InstallAsync(Action<string> onProgress)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Legion-Fan-Controller");

                onProgress?.Invoke("Downloading PawnIO Setup...");
                string setupPath = Path.Combine(Path.GetTempPath(), "PawnIO.Setup.exe");
                await DownloadFileAsync(client, SetupUrl, setupPath);

                onProgress?.Invoke("Installing PawnIO Driver (this may take a moment)...");
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = setupPath,
                    Arguments = "-install -silent",
                    UseShellExecute = true,
                    Verb = "runas"
                });
                if (process != null) await process.WaitForExitAsync();

                onProgress?.Invoke("Fetching latest Modules download link...");
                string modulesUrl = await GetLatestModulesUrl(client);
                if (string.IsNullOrEmpty(modulesUrl))
                {
                    throw new Exception("Could not find the latest Modules download link in GitHub API.");
                }

                onProgress?.Invoke("Downloading Modules...");
                string zipPath = Path.Combine(Path.GetTempPath(), "PawnIO.Modules.zip");
                await DownloadFileAsync(client, modulesUrl, zipPath);

                onProgress?.Invoke("Extracting LpcIO.bin...");
                string extractPath = Path.Combine(Path.GetTempPath(), "PawnIOExtract");
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);

                string sourceBin = Path.Combine(extractPath, LpcIoBin);
                string destBin = Path.Combine(DestDir, LpcIoBin);

                if (!Directory.Exists(DestDir))
                {
                    Directory.CreateDirectory(DestDir);
                }

                onProgress?.Invoke("Finalizing installation...");
                File.Copy(sourceBin, destBin, true);

                // Cleanup
                try { File.Delete(setupPath); } catch { }
                try { File.Delete(zipPath); } catch { }
                try { Directory.Delete(extractPath, true); } catch { }

                return true;
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"Error: {ex.Message}");
                return false;
            }
        }

        private static async Task<string> GetLatestModulesUrl(HttpClient client)
        {
            var response = await client.GetStringAsync(ModulesApiUrl);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("browser_download_url", out var url) && url.ValueKind != JsonValueKind.Null)
                    {
                        string? downloadUrl = url.GetString();
                        if (!string.IsNullOrEmpty(downloadUrl) && downloadUrl.EndsWith(".zip"))
                        {
                            return downloadUrl;
                        }
                    }
                }
            }
            return string.Empty;
        }

        private static async Task DownloadFileAsync(HttpClient client, string url, string path)
        {
            var data = await client.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(path, data);
        }
    }
}
