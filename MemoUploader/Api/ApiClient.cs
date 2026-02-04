using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MemoEngine.Models;
using MemoUploader.Helpers;
using Newtonsoft.Json;


namespace MemoUploader.Api;

internal static class ApiClient
{
    private static readonly HttpClient Client;

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

    public static async Task<bool> UploadFight(FightRecordPayload payload)
    {
        var json  = JsonConvert.SerializeObject(payload);
        var tasks = ApiUrls.Select(apiUrl => UploadFightToUrl(apiUrl, json)).ToList();
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

    private static async Task<bool> UploadFightToUrl(string apiUrl, string json)
    {
        var       url = $"{apiUrl}/fight";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            LogHelper.Info($"Fight record body: {json}");
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp    = await Client.PostAsync(url, content, cts.Token);
            if (resp.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
            {
                LogHelper.Info($"Fight record uploaded successfully to {apiUrl}");
                return true;
            }
            var err = await resp.Content.ReadAsStringAsync();
            LogHelper.Warning($"Fight record upload to {apiUrl} failed: [{resp.StatusCode}] {err}");
            return false;
        }
        catch (Exception e)
        {
            LogHelper.Warning($"Fight record upload to {apiUrl} exception: {e.Message}");
            return false;
        }
    }
}
