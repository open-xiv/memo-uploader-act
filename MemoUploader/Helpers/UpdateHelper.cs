using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;


namespace MemoUploader.Helpers;

internal class UpdateHelper
{
    private const string BaseUrl = "https://haku.diemoe.net/memo-uploader";

    private const string ManifestUrl = $"{BaseUrl}/manifest.json";

    private readonly string     pluginPath;
    private readonly string     pluginDir;
    private readonly HttpClient httpClient;

    private Version? LocalVersion  { get; }
    private Version? LatestVersion { get; set; }
    private string?  DownloadUrl   { get; set; }

    public bool HasUpdate => LatestVersion > LocalVersion;

    public UpdateHelper(string? pluginPath, string? pluginDir)
    {
        this.pluginPath = pluginPath ?? throw new ArgumentNullException(nameof(pluginPath));
        this.pluginDir  = pluginDir ?? throw new ArgumentNullException(nameof(pluginDir));

        LocalVersion = Assembly.GetExecutingAssembly().GetName().Version;

        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MemoUploaderACT", LocalVersion.ToString()));
        httpClient.Timeout = TimeSpan.FromSeconds(15);

        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
    }

    public async Task CheckForUpdatesAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(ManifestUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogHelper.Warning($"Check update failed: HTTP {response.StatusCode}");
                return;
            }

            var json     = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var manifest = JObject.Parse(json);

            var versionStr = manifest["version"]?.ToString();
            if (string.IsNullOrWhiteSpace(versionStr))
            {
                LogHelper.Warning("Check update failed: version field missing in manifest");
                return;
            }

            if (versionStr!.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                versionStr = versionStr.Substring(1);

            if (Version.TryParse(versionStr, out var remoteVer))
            {
                LatestVersion = remoteVer;

                var jsonUrl = manifest["downloadUrl"]?.ToString() ?? string.Empty;
                DownloadUrl = !string.IsNullOrWhiteSpace(jsonUrl) ? jsonUrl : $"{BaseUrl}/MemoUploader-v{remoteVer}.zip";

                LogHelper.Debug($"Check update success: [local {LocalVersion}] [latest {LatestVersion}]");
            }
        }
        catch (Exception ex) { LogHelper.Error($"Check update failed: {ex.Message}"); }
    }

    public async Task<bool> PerformUpdateAsync(CancellationToken ct = default)
    {
        if (!HasUpdate || string.IsNullOrEmpty(DownloadUrl))
            return false;

        var zipPath     = Path.Combine(pluginDir, "update_temp.zip");
        var extractPath = Path.Combine(pluginDir, "update_temp_extract");

        LogHelper.Debug($"Update planned: [version {LatestVersion}] [url {DownloadUrl}]");
        LogHelper.Debug($"Target path: [zip {zipPath}] [extract {extractPath}]");

        try
        {
            using (var response = await httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    LogHelper.Error($"Download update failed: HTTP {response.StatusCode}");
                    throw new HttpRequestException($"HTTP {response.StatusCode}");
                }

                using var stream     = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream, 81920, ct).ConfigureAwait(false);
            }

            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
            ZipFile.ExtractToDirectory(zipPath, extractPath);

            var newDllPath = Path.Combine(extractPath, Path.GetFileName(pluginPath));
            if (File.Exists(newDllPath))
            {
                var downloadedVersionInfo = FileVersionInfo.GetVersionInfo(newDllPath);
                if (Version.TryParse(downloadedVersionInfo.FileVersion, out var downloadedVer))
                {
                    if (downloadedVer <= LocalVersion)
                    {
                        LogHelper.Info($"Update cancelled: downloaded version ({downloadedVer}) <= ({LocalVersion})");
                        CleanupTempFiles(zipPath, extractPath);
                        return false;
                    }
                }
            }

            ApplyUpdateRecursive(extractPath, pluginDir);

            LogHelper.Info($"Update success: updated to version {LatestVersion}");
            return true;
        }
        catch (Exception ex)
        {
            LogHelper.Error($"Update failed: {ex.Message}");
            return false;
        }
        finally { CleanupTempFiles(zipPath, extractPath); }
    }

    private static void ApplyUpdateRecursive(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(targetDir, fileName);

            if (File.Exists(destFile))
            {
                var oldFile = destFile + ".old";
                try
                {
                    if (File.Exists(oldFile))
                        File.Delete(oldFile);

                    File.Move(destFile, oldFile);
                }
                catch
                {
                    LogHelper.Warning($"Apply updates to file failed: {fileName}");
                    continue;
                }
            }

            File.Move(file, destFile);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName    = Path.GetFileName(dir);
            var destSubDir = Path.Combine(targetDir, dirName);
            ApplyUpdateRecursive(dir, destSubDir);
        }
    }

    private static void CleanupTempFiles(string zipPath, string extractPath)
    {
        try
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
        }
        catch
        {
            // ignored
        }
    }
}
