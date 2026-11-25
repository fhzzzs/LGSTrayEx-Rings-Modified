using Xunit;
namespace LGSTrayUI.Tests;
public class RingsUpdateTests{
    [Theory]
    [InlineData("1.0.0+abc1234.20261125T040000Z",1,0,0)]
    [InlineData("3.2.6-rings.15.0.0+abc1234",15,0,0)]
    [InlineData("1.0.1",1,0,1)]
    public void BuildVersionUnderstandsPublicAndLegacyVersions(string value,int major,int minor,int patch)
        =>Assert.Equal(new System.Version(major,minor,patch),RingsUpdateService.ParseBuildVersion(value));

    [Theory][InlineData("rings-1.0.0",true)][InlineData("rings-10.0.1",true)][InlineData("rings-11.0.0",true)][InlineData("v10.0.1",false)][InlineData("rings-10.0.1-beta",false)][InlineData("rings-../10.0.1",false)]
    public void OnlyReleaseTagsAreAccepted(string tag,bool expected)=>Assert.Equal(expected,RingsUpdateService.TryTag(tag,out _));
    [Fact]public void BrandedReleaseAssetIsTrusted(){
        const string url="https://github.com/fhzzzs/LGSTrayEx-Rings-Modified/releases/download/rings-1.0.3/Battery-Monitor-Windows-x64.zip";
        Assert.True(RingsUpdateService.IsTrustedAsset("rings-1.0.3",url));
        Assert.False(RingsUpdateService.IsTrustedAsset("rings-1.0.4",url));
    }
    [Fact]public void OnlyRepositoryReleaseAssetIsTrusted(){
        const string path="/fhzzzs/LGSTrayEx-Rings-Modified/releases/download/rings-10.0.1/LGSTrayEx-Rings-Portable-x64.zip";
        Assert.True(RingsUpdateService.IsTrustedAsset("rings-10.0.1","https://github.com"+path));
        Assert.False(RingsUpdateService.IsTrustedAsset("rings-10.0.1","https://example.com"+path));
        Assert.False(RingsUpdateService.IsTrustedAsset("rings-10.0.2","https://github.com"+path));
    }
    [Theory][InlineData("LGSTray.exe",true)][InlineData("runtimes/win-x64/test.dll",true)][InlineData("../LGSTray.exe",false)][InlineData("C:/evil.exe",false)][InlineData("safe/../../evil.exe",false)][InlineData("NUL",false)][InlineData("a.txt:alternate",false)]
    public void ArchiveEntriesCannotEscapeVersionDirectory(string entry,bool expected)=>Assert.Equal(expected,RingsUpdateService.SafeEntry(entry));
}
