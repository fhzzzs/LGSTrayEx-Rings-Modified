using LGSTrayHID.Protocol;
using Xunit;
namespace LGSTrayHID.Tests;
public class LegacyBatteryRegisterTests{
    [Fact]public void OnlyExactSlotAndValidMileageAreAccepted(){
        byte[] r=[0x10,1,0x81,0x0D,72,0,0x30];
        Assert.Equal(72,LegacyBatteryRegister.DecodeMileage(1,r)!.Value.batteryPercentage);
        Assert.Null(LegacyBatteryRegister.DecodeMileage(2,r));
        r[4]=255;Assert.Null(LegacyBatteryRegister.DecodeMileage(1,r));
        r[4]=72;r[6]=0x50;Assert.Null(LegacyBatteryRegister.DecodeMileage(1,r));
    }
    [Fact]public void LegacyIdentityRequiresReceiverAnnouncement(){
        Assert.NotNull(LegacyBatteryRegister.Announcement([0x10,1,0x41,0,2,0x12,0x40]));
        Assert.Null(LegacyBatteryRegister.Announcement([0x20,1,0x41,0,2,0x12,0x40]));
        Assert.Null(LegacyBatteryRegister.Announcement([0x10,1,0x41,0,0x42,0x12,0x40]));
    }
}
