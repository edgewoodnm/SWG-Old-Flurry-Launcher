using System.Net.Http;
using System.IO;
using System.Text.Json;

namespace SwgOldFlurryLauncher;

public sealed class ServerStatusService : IDisposable
{
    public const string Feed="http://73.127.195.38/flurry-status/status.json";
    public const string InternalFeed="http://10.0.0.220/flurry-status/status.json";
    readonly HttpClient http;
    public ServerStatusService(HttpMessageHandler? handler=null){http=handler==null?new HttpClient():new HttpClient(handler);http.Timeout=TimeSpan.FromSeconds(4);}
    public static string Parse(string json,DateTimeOffset now)
    {
        try
        {
            using var document=JsonDocument.Parse(json);var root=document.RootElement;
            if(!root.TryGetProperty("checked_at",out var timestamp)||!DateTimeOffset.TryParse(timestamp.GetString(),out var checkedAt))return "UNAVAILABLE";
            var age=now-checkedAt.ToUniversalTime();if(age>TimeSpan.FromMinutes(2)||age<TimeSpan.FromSeconds(-30))return "UNAVAILABLE";
            string? status=root.GetProperty("status").GetString();return status is "ONLINE" or "OFFLINE"?status:"UNAVAILABLE";
        }
        catch(Exception e)when(e is JsonException or InvalidOperationException or KeyNotFoundException){return "UNAVAILABLE";}
    }
    public async Task<string> Read()
    {
        foreach(string endpoint in new[]{Feed,InternalFeed})
        {
            string status=await ReadFrom(endpoint);
            if(status is "ONLINE" or "OFFLINE")return status;
        }
        return "UNAVAILABLE";
    }
    async Task<string> ReadFrom(string endpoint)
    {
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var response=await http.GetAsync(endpoint+"?t="+DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer=new MemoryStream();byte[] bytes=new byte[1024];int count;
            while((count=await stream.ReadAsync(bytes,timeout.Token))>0){if(buffer.Length+count>4096)return "UNAVAILABLE";buffer.Write(bytes,0,count);}
            return Parse(System.Text.Encoding.UTF8.GetString(buffer.ToArray()),DateTimeOffset.UtcNow);
        }
        catch(Exception e)when(e is HttpRequestException or OperationCanceledException or IOException){return "UNAVAILABLE";}
    }
    public void Dispose()=>http.Dispose();
}
