using System.Text.Json;
var input=await Console.In.ReadLineAsync();using var request=JsonDocument.Parse(input);
var mode=request.RootElement.GetProperty("Settings").GetProperty("mode").GetString();
if(mode=="slow")await Task.Delay(30000);
if(mode=="oversize"){Console.WriteLine(new string('x',262150));return;}
if(mode=="invalid"){Console.WriteLine("bad-json");return;}
Console.WriteLine(JsonSerializer.Serialize(new{ApiVersion=1,Id=request.RootElement.GetProperty("Id").GetString(),Results=Array.Empty<object>(),Action="clipboard",Value="unapproved"}));
