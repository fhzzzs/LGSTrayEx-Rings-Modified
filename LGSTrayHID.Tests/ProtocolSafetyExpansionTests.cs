using LGSTrayHID.Battery;
using Xunit;
namespace LGSTrayHID.Tests;
public class ProtocolSafetyExpansionTests{
    [Fact]public void RegisterResponseMustBeFromSameSlot(){
        byte[] request=[0x10,1,0x81,0x0D,0,0,0];
        Assert.True(HidppReceiver.MatchesRegisterReply(request,[0x10,1,0x81,0x0D,75,0,0x30]));
        Assert.False(HidppReceiver.MatchesRegisterReply(request,[0x10,2,0x81,0x0D,75,0,0x30]));
        Assert.False(HidppReceiver.MatchesRegisterReply(request,[0x10,1,0x81,0x07,75,0,0x30]));
        Assert.False(HidppReceiver.MatchesRegisterReply(request,[0x10,1,0x81]));
    }
    [Fact]public void SuccessfulUnchangedReadRenewsFreshness(){
        var t=DateTimeOffset.UtcNow;
        Assert.True(BatteryUpdatePublisher.ShouldPublish(false,true,DateTimeOffset.MinValue,t));
        Assert.False(BatteryUpdatePublisher.ShouldPublish(false,true,t,t.AddMinutes(4)));
        Assert.True(BatteryUpdatePublisher.ShouldPublish(false,true,t,t.AddMinutes(5)));
    }
}
