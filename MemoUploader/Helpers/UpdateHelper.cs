using System;
using System.Diagnostics;
using System.IO;
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

    private readonly string     pluginPath;
    private readonly HttpClient httpClient;

    public Version LocalVersion  { get; }
    public Version LatestVersion { get; private set; }
    public string  DownloadUrl   { get; private set; }

    public bool HasUpdate => LatestVersion > LocalVersion;

    public UpdateHelper(string? pluginPath)
    {
        this.pluginPath = pluginPath ?? throw new ArgumentNullException(nameof(pluginPath));

        LocalVersion = Assembly.GetExecutingAssembly().GetName().Version;

        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MemoUploaderACT", LocalVersion.ToString()));
        httpClient.Timeout = TimeSpan.FromSeconds(10);

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
                DownloadUrl = !string.IsNullOrWhiteSpace(jsonUrl) ? jsonUrl : $"{BaseUrl}/MemoUploader.dll";
                LogHelper.Debug($"检查更新完成: 本地版本 {LocalVersion} 最新版本 {LatestVersion}");
            }
        }
        catch (Exception ex) { LogHelper.Error($"更新检查失败: {ex.Message}"); }
    }

    public async Task<bool> PerformUpdateAsync(CancellationToken ct = default)
    {
        if (!HasUpdate || string.IsNullOrEmpty(DownloadUrl))
            return false;

        var tempFilePath = pluginPath + ".new";
        var backupPath   = pluginPath + ".old";
        LogHelper.Debug($"计划更新: 版本 {LatestVersion} 下载地址 {DownloadUrl} 临时文件 {tempFilePath} 备份文件 {backupPath}");

        try
        {
            using (var response = await httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"HTTP {response.StatusCode}");

                using var stream     = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream, 81920, ct).ConfigureAwait(false);
            }

            var downloadedVersionInfo = FileVersionInfo.GetVersionInfo(tempFilePath);
            if (Version.TryParse(downloadedVersionInfo.FileVersion, out var downloadedVer))
            {
                if (downloadedVer <= LocalVersion)
                {
                    File.Delete(tempFilePath);
                    return false;
                }
            }

            if (File.Exists(backupPath))
            {
                try { File.Delete(backupPath); }
                catch
                {
                    // ignored
                }
            }

            try { File.Move(pluginPath, backupPath); }
            catch (IOException)
            {
                File.Delete(tempFilePath);
                return false;
            }

            File.Move(tempFilePath, pluginPath);
            return true;
        }
        catch (Exception ex)
        {
            LogHelper.Error($"更新失败: {ex.Message}");
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
            return false;
        }
    }
}
