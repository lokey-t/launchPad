using LaunchPad.PluginSdk;
await Runner.Run(request=>{
 string root=request.Settings.GetValueOrDefault("folder");
 if(string.IsNullOrWhiteSpace(root))root=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
 if(request.Method=="search"){
  try{
   if(!Directory.Exists(root))return Task.FromResult(new Response());
   var results=Directory.EnumerateFileSystemEntries(root).Take(500).Where(p=>Path.GetFileName(p).Contains(request.Query??"",StringComparison.OrdinalIgnoreCase)).Take(20).Select(p=>new Result{Title=Path.GetFileName(p),Description=p,Command="open",Value=Path.GetFullPath(p)}).ToList();
   return Task.FromResult(new Response{Results=results});
  }catch{return Task.FromResult(new Response{Message="Folder unavailable / 文件夹不可访问"});}
 }
 return Task.FromResult(request.Command switch {
  "copy-path"=>new Response{Action=request.ContextPath!=null?"clipboard":null,Value=request.ContextPath,Message="Copy path / 复制路径"},
  "parent"=>new Response{Action=request.ContextPath!=null?"open":null,Value=request.ContextPath!=null?Path.GetDirectoryName(request.ContextPath):null},
  "open"=>new Response{Action="open",Value=request.Value??root},
  _=>new Response{Message="Unknown command / 未知操作"}
 });
});
