using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LGSTrayPrimitives;

namespace LGSTrayUI;

internal sealed record RingsRelease(string Tag,Version Version,Uri Download,long Size,string Hash,string Notes);

internal static class RingsUpdateService
{
    private const string Repo="fhzzzs/LGSTrayEx-Rings-Modified";
    internal const string Asset="LGSTrayEx-Rings-Portable-x64.zip";
    private const long MaxDownload=350L*1024*1024,MaxExpanded=900L*1024*1024;
    internal static readonly string Versions=Path.Combine(AppDataPaths.LocalAppDataDir,"versions");
    private static readonly string Previous=Path.Combine(AppDataPaths.LocalAppDataDir,"rings-previous-version.json");
    private static readonly HttpClient Http=new(){Timeout=Timeout.InfiniteTimeSpan};
    static RingsUpdateService(){Http.DefaultRequestHeaders.UserAgent.ParseAdd("LGSTrayEx-Rings-Update/1.0.0");}
    internal static Version Current{
        get{
            var s=Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??"";
            return ParseBuildVersion(s);
        }
    }
    internal static Version ParseBuildVersion(string value){
        int pos=value.IndexOf("-rings.",StringComparison.Ordinal);
        var version=(pos>=0?value[(pos+7)..]:value).Split('+')[0];
        return Version.TryParse(version,out var parsed)?parsed:new Version(1,0,2);
    }
    internal static bool TryTag(string? tag,out Version version){
        version=new Version(0,0,0);
        if(tag==null||!Regex.IsMatch(tag,@"^rings-[0-9]+\.[0-9]+\.[0-9]+$"))return false;
        if(!Version.TryParse(tag[6..],out var parsed))return false;
        version=parsed;return true;
    }
    internal static bool IsTrustedAsset(string tag,string? url)
        =>TryTag(tag,out _)&&string.Equals(url,$"https://github.com/{Repo}/releases/download/{tag}/{Asset}",StringComparison.Ordinal);
    internal static string? PreviousExecutable(){
        try{
            var path=JsonSerializer.Deserialize<string>(File.ReadAllText(Previous));
            return path!=null&&File.Exists(path)&&Path.GetFileName(path).Equals("LGSTray.exe",StringComparison.OrdinalIgnoreCase)?path:null;
        }catch{return null;}
    }
    internal static void RememberPrevious(string path)=>RingSettingsStore.WriteAtomic(Previous,JsonSerializer.Serialize(path));
    internal static async Task<RingsRelease?> CheckAsync(CancellationToken token){
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response=await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest",timeout.Token).ConfigureAwait(false);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        response.EnsureSuccessStatusCode();
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
        var root=doc.RootElement;string? tag=root.GetProperty("tag_name").GetString();
        if(!TryTag(tag,out var version)||version<=Current)return null;
        if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())return null;
        foreach(var asset in root.GetProperty("assets").EnumerateArray()){
            if(asset.GetProperty("name").GetString()!=Asset)continue;
            string? url=asset.GetProperty("browser_download_url").GetString();
            long size=asset.GetProperty("size").GetInt64();
            string? digest=asset.TryGetProperty("digest",out var d)?d.GetString():null;
            if(!IsTrustedAsset(tag!,url)||size<=0||size>MaxDownload||digest==null||!Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("发布包缺少有效 SHA-256 或下载地址，不允许安装。");
            return new(tag!,version,new Uri(url!),size,digest[7..],root.TryGetProperty("body",out var n)?n.GetString()??"":"");
        }
        throw new InvalidDataException("发布版本没有完整 Windows x64 安装包。");
    }
    internal static async Task<string> StageAsync(RingsRelease release,IProgress<int>? progress,CancellationToken token){
        if(release.Version<=Current||!IsTrustedAsset(release.Tag,release.Download.AbsoluteUri)||release.Size<=0||release.Size>MaxDownload||!Regex.IsMatch(release.Hash,@"^[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("无效更新包。");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(8));
        var ct=timeout.Token;
        Directory.CreateDirectory(Versions);
        string id=Guid.NewGuid().ToString("N"),zip=Path.Combine(Versions,id+".zip"),temp=Path.Combine(Versions,id+".stage");
        string destination=Path.Combine(Versions,release.Tag+"-"+id[..8]);
        try{
            using(var response=await Http.GetAsync(release.Download,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false)){
                response.EnsureSuccessStatusCode();
                using var input=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var output=new FileStream(zip,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
                byte[] buffer=new byte[81920];long total=0;
                while(true){
                    int count=await input.ReadAsync(buffer.AsMemory(),ct).ConfigureAwait(false);if(count==0)break;
                    total+=count;if(total>release.Size||total>MaxDownload)throw new InvalidDataException("更新下载大小异常。");
                    await output.WriteAsync(buffer.AsMemory(0,count),ct).ConfigureAwait(false);
                    progress?.Report((int)(100*total/release.Size));
                }
                if(total!=release.Size)throw new InvalidDataException("更新下载不完整。");
            }
            using(var input=File.OpenRead(zip)){
                string actual=Convert.ToHexString(await SHA256.HashDataAsync(input,ct).ConfigureAwait(false));
                if(!string.Equals(actual,release.Hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("更新包 SHA-256 校验失败。");
            }
            await Task.Run(()=>ExtractVerified(zip,temp,ct),ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            UpdateTransaction.PreserveConfiguration(AppContext.BaseDirectory,temp);
            File.WriteAllText(UpdateTransaction.MarkerPath(temp),release.Tag);
            Directory.Move(temp,destination);
            return Path.Combine(destination,"LGSTray.exe");
        }finally{
            try{if(File.Exists(zip))File.Delete(zip);}catch{}
            try{if(Directory.Exists(temp))Directory.Delete(temp,true);}catch{}
        }
    }
    internal static bool SafeEntry(string name){
        if(string.IsNullOrWhiteSpace(name)||name.Contains(':')||name.StartsWith('/')||name.StartsWith('\\'))return false;
        var parts=name.Replace('\\','/').TrimEnd('/').Split('/');
        return parts.All(x=>x.Length>0&&x!="."&&x!=".."&&!x.EndsWith('.')&&!x.EndsWith(' ')&&
            x.IndexOfAny(Path.GetInvalidFileNameChars())<0&&!Regex.IsMatch(x,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase));
    }
    private static void ExtractVerified(string zip,string folder,CancellationToken token){
        using(var archive=ZipFile.OpenRead(zip)){
            if(archive.Entries.Count>10000||archive.Entries.Sum(x=>x.Length)>MaxExpanded)throw new InvalidDataException("解压大小超出限制。");
            foreach(var entry in archive.Entries){
                token.ThrowIfCancellationRequested();
                if(!SafeEntry(entry.FullName)||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("更新包含不安全路径。");
            }
        }
        ZipFile.ExtractToDirectory(zip,folder);
        if(!PortableLayout.IsCompletePackage(folder))throw new InvalidDataException("更新包缺少启动程序或必要运行文件。");
    }
}
