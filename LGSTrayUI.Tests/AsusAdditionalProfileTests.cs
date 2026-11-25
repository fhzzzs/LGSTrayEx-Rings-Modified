using Xunit;
namespace LGSTrayUI.Tests;
public class AsusAdditionalProfileTests{
    [Fact]public void CetraComponentsDoNotUseCaseForAggregate(){
        byte[] bat=[0xCC,0x12,7,0,0,0,80,43,255];
        byte[] charge=[0xCC,0x12,8,0,0,2];
        var result=AsusBatteryReader.DecodeCetra(bat,charge)!.Value;
        Assert.Equal(80,result.Left);Assert.Equal(43,result.Right);Assert.Null(result.Case);Assert.True(result.Charging);
        bat[7]=255;Assert.Null(AsusBatteryReader.DecodeCetra(bat,charge));
    }
    [Fact]public void StrixCarryUsesDifferentOffsetAndInterface(){
        var profile=PeripheralCatalog.Find(0x18B4,AsusProtocol.DirectMouse)!;
        Assert.Equal(1,profile.InterfaceNumber);Assert.Equal(6,profile.BatteryByte);
        byte[] reply=[0x12,7,0,0,1,0,3,0,0,0];
        Assert.Equal((75,false),AsusBatteryReader.DecodeMouse(reply,profile.Scale,profile.BatteryByte)!.Value);
    }
}
