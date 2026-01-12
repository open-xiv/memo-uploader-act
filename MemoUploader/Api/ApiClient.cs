using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MemoUploader.Helpers;
using MemoUploader.Models;
using Newtonsoft.Json;


namespace MemoUploader.Api;

internal static class ApiClient
{
    private static readonly HttpClient Client;

    private const string AssetsUrl = "https://assets.sumemo.dev";
    private const string ApiUrl    = "https://api.sumemo.dev";
    private const string AuthKey   = ApiSecrets.AuthKey;

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

    public static async Task<DutyConfig?> FetchDutyConfigAsync(uint zoneId)
    {
        var url = $"{AssetsUrl}/duty/{zoneId}";
        try
        {
            var resp = await Client.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode != HttpStatusCode.NotFound)
                    LogHelper.Warning($"Fetch duty config failed: {resp.StatusCode}");
                return null;
            }

            var content = await resp.Content.ReadAsStringAsync();
            var duty    = JsonConvert.DeserializeObject<DutyConfig>(content);
            return duty;
        }
        catch (Exception) { return null; }
    }

    public static async Task<bool> UploadFightRecordAsync(FightRecordPayload payload)
    {
        const string url = $"{ApiUrl}/fight";
        try
        {
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            var resp = await Client.PostAsync(url, content);
            if (resp.StatusCode == HttpStatusCode.Created)
            {
                LogHelper.Info("Fight record uploaded successfully");
                return true;
            }
            LogHelper.Warning($"Fight record upload failed: {resp.StatusCode}");
            LogHelper.Warning(resp.Content.ReadAsStringAsync().Result);
            return false;
        }
        catch (Exception) { return false; }
    }
}
