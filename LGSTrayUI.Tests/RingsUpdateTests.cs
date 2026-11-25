using Xunit;
namespace LGSTrayUI.Tests;
public class RingsUpdateTests{
    [Theory][InlineData("rings-10.0.1",true)][InlineData("rings-11.0.0",true)][InlineData("v10.0.1",false)][InlineData("rings-10.0.1-beta",false)][InlineData("rings-../10.0.1",false)]
    public void OnlyReleaseTagsAreAccepted(string tag,bool expected)=>Assert.Equal(expected,RingsUpdateService.TryTag(tag,out _));
    [Fact]public void OnlyRepositoryReleaseAssetIsTrusted(){
        const string path="/fhzzzs/LGSTrayEx-Rings-Modified/releases/download/rings-10.0.1/LGSTrayEx-Rings-Portable-x64.zip";
        Assert.True(RingsUpdateService.IsTrustedAsset("rings-10.0.1","https://github.com"+path));
        Assert.False(RingsUpdateService.IsTrustedAsset("rings-10.0.1","https://example.com"+path));
        Assert.False(RingsUpdateService.IsTrustedAsset("rings-10.0.2","https://github.com"+path));
    }
    [Theory][InlineData("LGSTray.exe",true)][InlineData("runtimes/win-x64/test.dll",true)][InlineData("../LGSTray.exe",false)][InlineData("C:/evil.exe",false)][InlineData("safe/../../evil.exe",false)][InlineData("NUL",false)][InlineData("a.txt:alternate",false)]
    public void ArchiveEntriesCannotEscapeVersionDirectory(string entry,bool expected)=>Assert.Equal(expected,RingsUpdateService.SafeEntry(entry));
}
