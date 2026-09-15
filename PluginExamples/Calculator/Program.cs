using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using LaunchPad.PluginSdk;
await Runner.Run(request=>{
 if(request.Method=="execute")return Task.FromResult(new Response{Action=request.Value!=null?"clipboard":null,Value=request.Value,Message=request.Value!=null?"Copied / 已复制":"Type = followed by an expression / 输入 = 后跟算式"});
 var expression=request.Query??"";
 if(expression.Length>120||!Regex.IsMatch(expression,@"^[0-9. +*/()\-]+$"))return Task.FromResult(new Response());
 try{
  var result=Convert.ToDouble(new DataTable().Compute(expression,""),CultureInfo.InvariantCulture);
  if(!double.IsFinite(result))return Task.FromResult(new Response());
  int digits=int.TryParse(request.Settings.GetValueOrDefault("digits"),out var d)?Math.Clamp(d,0,10):4;
  var value=Math.Round(result,digits).ToString(CultureInfo.InvariantCulture);
  return Task.FromResult(new Response{Results=new(){new(){Title=value,Description=expression+" · Click to copy / 点击复制",Command="copy",Value=value}}});
 }catch{return Task.FromResult(new Response());}
});
