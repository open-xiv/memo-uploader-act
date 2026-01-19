using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MemoUploader.Helpers;
using MemoUploader.Models;
using Newtonsoft.Json;


namespace MemoUploader.Api;

internal static class ApiClient
{
    private static readonly HttpClient Client;

    private static readonly string[] AssetUrls =
    [
        "https://assets.sumemo.dev",
        "https://haku.diemoe.net/assets"
    ];

    private static readonly string[] ApiUrls =
    [
        "https://api.sumemo.dev",
        "https://sumemo.diemoe.net"
    ];

    private const string AuthKey = ApiSecrets.AuthKey;

    static ApiClient()
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        var handler = new HttpClientHandler
        {
            AutomaticDecompression  = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxConnectionsPerServer = 4,
            UseProxy                = false
        };

        Client = new HttpClient(handler);
        Client.DefaultRequestHeaders.Add("X-Auth-Key", AuthKey);
        Client.Timeout = TimeSpan.FromSeconds(5);
    }

    public static async Task<DutyConfig?> FetchDuty(uint zoneId)
    {
        var tasks = AssetUrls.Select(assetUrl => FetchDutyFromUrl(assetUrl, zoneId)).ToList();
        while (tasks.Count > 0)
        {
            var complete = await Task.WhenAny(tasks);
            var result   = await complete;
            if (result is not null)
                return result;
            tasks.Remove(complete);
        }
        return null;
    }

    private static async Task<DutyConfig?> FetchDutyFromUrl(string assetUrl, uint zoneId)
    {
        var       url = $"{assetUrl}/duty/{zoneId}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var resp = await Client.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == HttpStatusCode.NotFound)
                    return null;
                LogHelper.Warning($"Duty [{zoneId}] timeline fetch from {assetUrl} failed: {resp.StatusCode}");
                return null;
            }

            var content = await resp.Content.ReadAsStringAsync();
            LogHelper.Warning($"Duty [{zoneId}] timeline fetched from {assetUrl}");
            return JsonConvert.DeserializeObject<DutyConfig>(content);
        }
        catch (Exception) { return null; }
    }

    public static async Task<bool> UploadFight(FightRecordPayload payload)
    {
        var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
        var tasks   = ApiUrls.Select(apiUrl => UploadFightToUrl(apiUrl, content)).ToList();
        while (tasks.Count > 0)
        {
            var complete = await Task.WhenAny(tasks);
            var result   = await complete;
            if (result)
                return true;
            tasks.Remove(complete);
        }
        return false;
    }

    private static async Task<bool> UploadFightToUrl(string apiUrl, StringContent content)
    {
        var       url = $"{apiUrl}/fight";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var resp = await Client.PostAsync(url, content, cts.Token);
            if (resp.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
            {
                LogHelper.Info($"Fight record uploaded successfully to {apiUrl}");
                return true;
            }
            var err = await resp.Content.ReadAsStringAsync();
            LogHelper.Warning($"Fight record upload to {apiUrl} failed: [{resp.StatusCode}] {err}");
            return false;
        }
        catch (Exception) { return false; }
    }
}
