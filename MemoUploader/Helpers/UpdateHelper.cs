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

public class UpdateHelper
{
    private const string BaseUrl = "https://haku.diemoe.net/memo-uploader";

    private const string ManifestUrl = $"{BaseUrl}/manifest.json";

    private readonly string     pluginPath; // DLL 文件路径
    private readonly string     pluginDir;  // 插件根目录
    private readonly HttpClient httpClient;

    public Version LocalVersion  { get; }
    public Version LatestVersion { get; private set; }
    public string  DownloadUrl   { get; private set; }

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
                return;

            var json     = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var manifest = JObject.Parse(json);

            var versionStr = manifest["version"]?.ToString();
            if (string.IsNullOrWhiteSpace(versionStr))
                return;

            if (versionStr.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                versionStr = versionStr.Substring(1);

            if (Version.TryParse(versionStr, out var remoteVer))
            {
                LatestVersion = remoteVer;

                var jsonUrl = manifest["downloadUrl"]?.ToString() ?? string.Empty;
                DownloadUrl = !string.IsNullOrWhiteSpace(jsonUrl) ? jsonUrl : $"{BaseUrl}/MemoUploader-v{remoteVer}.zip";

                LogHelper.Debug($"检查更新完成: 本地版本 {LocalVersion} 最新版本 {LatestVersion}");
            }
        }
        catch (Exception ex) { LogHelper.Error($"更新检查失败: {ex.Message}"); }
    }

    public async Task<bool> PerformUpdateAsync(CancellationToken ct = default)
    {
        if (!HasUpdate || string.IsNullOrEmpty(DownloadUrl))
            return false;

        var zipPath     = Path.Combine(pluginDir, "update_temp.zip");
        var extractPath = Path.Combine(pluginDir, "update_temp_extract");

        LogHelper.Debug($"计划更新: 版本 {LatestVersion} 下载地址 {DownloadUrl}");
        LogHelper.Debug($"目标下载路径: {zipPath} 解压路径: {extractPath}");

        try
        {
            using (var response = await httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"HTTP {response.StatusCode}");

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
                        LogHelper.Error($"更新取消: 下载的版本 ({downloadedVer}) 不高于本地版本 ({LocalVersion})");
                        CleanupTempFiles(zipPath, extractPath);
                        return false;
                    }
                }
            }

            ApplyUpdateRecursive(extractPath, pluginDir);

            LogHelper.Debug("更新应用成功 重启后生效");
            return true;
        }
        catch (Exception ex)
        {
            LogHelper.Error($"更新失败: {ex.Message}");
            return false;
        }
        finally { CleanupTempFiles(zipPath, extractPath); }
    }

    private void ApplyUpdateRecursive(string sourceDir, string targetDir)
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
                    LogHelper.Debug($"警告: 无法移动/替换文件 {fileName} (可能被占用)");
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

    private void CleanupTempFiles(string zipPath, string extractPath)
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
