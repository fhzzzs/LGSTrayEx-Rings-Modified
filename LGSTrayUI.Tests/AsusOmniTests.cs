using Xunit;
namespace LGSTrayUI.Tests;
public class AsusOmniTests{
    [Theory]
    [InlineData(2,0xFF01,1,true)]
    [InlineData(2,0xFF01,3,true)]
    [InlineData(2,0xFF02,1,true)]
    [InlineData(2,0xFF00,1,false)]
    [InlineData(0,0xFF01,1,false)]
    public void OmniAcceptsObservedMouseUsage(int iface,int page,int usage,bool expected)
        =>Assert.Equal(expected,AsusBatteryReader.IsOmniChannel(iface,(ushort)page,(ushort)usage));
    [Fact]public void PairListMayContainNonzeroStatusByte(){
        byte[] reply=[1,0xA0,0x04,0,0,0x65,0x1B,0,0];
        Assert.True(AsusBatteryReader.ExpectedReply(reply,reply.Length,1,0xA0,0));
        Assert.True(AsusBatteryReader.PairedHarpe(reply));
        Assert.False(AsusBatteryReader.PairedHarpe([1,0xA0,0,0,0,0x68,0x1B,0,0]));
    }
    [Fact]public void ReceiverInstanceMatchesOnlyCollectionsOfSameReceiver(){
        var pair=@"\\?\hid#vid_0b05&pid_1ace&mi_02&col01#7&abc&0&0001#{guid}";
        var mouse=@"\\?\hid#vid_0b05&pid_1ace&mi_02&col03#7&abc&0&0003#{guid}";
        var other=@"\\?\hid#vid_0b05&pid_1ace&mi_02&col03#7&def&0&0003#{guid}";
        Assert.NotNull(AsusBatteryReader.ReceiverInstance(pair));
        Assert.Equal(AsusBatteryReader.ReceiverInstance(pair),AsusBatteryReader.ReceiverInstance(mouse));
        Assert.NotEqual(AsusBatteryReader.ReceiverInstance(pair),AsusBatteryReader.ReceiverInstance(other));
        Assert.Null(AsusBatteryReader.ReceiverInstance("unrelated"));
    }
    [Fact]public void BatteryReplyMustMatchActualCommandAndValidPercent(){
        var valid=new byte[]{3,0x12,0x07,0,0,53,0,0,0,0,0};
        Assert.True(AsusBatteryReader.ExpectedReply(valid,valid.Length,3,0x12,0x07));
        Assert.Equal((53,false),AsusBatteryReader.Decode(valid)!.Value);
        valid[5]=101;Assert.Null(AsusBatteryReader.Decode(valid));
        valid[5]=0;Assert.Null(AsusBatteryReader.Decode(valid));
    }
}
