using System.Linq;
using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class AsusExpandedTests{
    [Fact]public void OmnipairCanContainKeyboardAndMouse(){
        var pids=AsusBatteryReader.PairedModels([1,0xA0,0,0,0,0x65,0x1B,0,0,0x06,0x1B,0,0,0,0]);
        Assert.Equal(new ushort[]{0x1B65,0x1B06},pids);
        Assert.Equal(DeviceType.Mouse,PeripheralCatalog.Find(pids[0],AsusProtocol.OmniMouse)!.Type);
        Assert.Equal(11,PeripheralCatalog.Find(pids[1],AsusProtocol.OmniKeyboard)!.BatteryByte);
    }
    [Fact]public void DirectMouseSupportsNumberedAndUnnumberedZeroReport(){
        byte[] frame=[0x12,7,0,0,3,0,0,0,0,1];
        Assert.Equal((75,true),AsusBatteryReader.DecodeMouse(frame,25)!.Value);
        Assert.Equal((3,true),AsusBatteryReader.DecodeMouse(new byte[]{0}.Concat(frame).ToArray(),1)!.Value);
        frame[4]=5;Assert.Null(AsusBatteryReader.DecodeMouse(frame,25));
    }
    [Fact]public void FalchionUsesDifferentBatteryByte(){
        byte[] frame=[2,0x12,1,0,0,0,7,0,0,1,0,74];
        Assert.Equal((74,true),AsusBatteryReader.DecodeKeyboard(frame,11)!.Value);
        Assert.Equal((7,true),AsusBatteryReader.DecodeKeyboard(frame,6)!.Value);
        Assert.Null(AsusBatteryReader.DecodeKeyboard(frame[..9],11));
    }
    [Fact]public void HeadsetVoltageIsAnEstimateNotTheConstantByte(){
        byte[] frame=[0xFF,0x1B,0,0,0,0,0,0,0,3,0,0xF7,0x0E,0x40];
        Assert.Equal((75,false),AsusBatteryReader.DecodeHeadset(frame)!.Value);
        frame[1]=0;Assert.Null(AsusBatteryReader.DecodeHeadset(frame));
    }
    [Fact]public void UnknownModelHasNoWriteProfile(){
        Assert.Null(PeripheralCatalog.Find(0xFFFF,AsusProtocol.DirectMouse));
        Assert.Null(PeripheralCatalog.Find(0x1CEA,AsusProtocol.OmniKeyboard));
    }
}
