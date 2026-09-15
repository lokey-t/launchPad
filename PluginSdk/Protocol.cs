using System.Text.Json;
namespace LaunchPad.PluginSdk;
public sealed class Request
{
    public int ApiVersion { get; set; } = 1;
    public string Id { get; set; }
    public string Method { get; set; }
    public string Query { get; set; }
    public string Command { get; set; }
    public string Value { get; set; }
    public string ContextPath { get; set; }
    public string DataDirectory { get; set; }
    public Dictionary<string,string> Settings { get; set; } = new();
}
public sealed class Result
{
    public string Title { get; set; }
    public string Description { get; set; }
    public string Command { get; set; }
    public string Value { get; set; }
}
public sealed class Response
{
    public int ApiVersion { get; set; } = 1;
    public string Id { get; set; }
    public List<Result> Results { get; set; } = new();
    public string Message { get; set; }
    public string Action { get; set; }
    public string Value { get; set; }
}
public static class Runner
{
    public static async Task Run(Func<Request,Task<Response>> handle)
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var line=await Console.In.ReadLineAsync();
        if(line==null || line.Length>262144)return;
        var request=JsonSerializer.Deserialize<Request>(line);
        if(request?.ApiVersion!=1)return;
        var response=await handle(request);
        response.Id=request.Id;
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response));
    }
}
