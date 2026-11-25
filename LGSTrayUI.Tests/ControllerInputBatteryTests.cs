using Xunit;
namespace LGSTrayUI.Tests;
public class ControllerInputBatteryTests{
    [Fact]public void DualSenseOnlyFullReportAndValidStatus(){
        byte[] report=new byte[64];report[0]=1;report[53]=0x17;
        Assert.Equal((75,true,false),ControllerInputBattery.Decode(0x054C,0x0CE6,report,64)!.Value);
        report[53]=0xF7;Assert.Null(ControllerInputBattery.Decode(0x054C,0x0CE6,report,64));
        Assert.Null(ControllerInputBattery.Decode(0x054C,0x0CE6,report,10));
    }
    [Fact]public void DualShockRecognizesDockAndBatteryBand(){
        byte[] report=new byte[64];report[0]=1;report[30]=0x1B;
        Assert.Equal((100,false,true),ControllerInputBattery.Decode(0x054C,0x09CC,report,64)!.Value);
        report[30]=6;Assert.Equal((65,false,false),ControllerInputBattery.Decode(0x054C,0x05C4,report,64)!.Value);
    }
    [Fact]public void NintendoRejectsSimpleReportsAndUnknownBands(){
        byte[] report=new byte[49];report[0]=0x30;report[2]=0x70;
        Assert.Equal((75,true,false),ControllerInputBattery.Decode(0x057E,0x2009,report,49)!.Value);
        report[0]=0x3F;Assert.Null(ControllerInputBattery.Decode(0x057E,0x2009,report,49));
        report[0]=0x30;report[2]=0xE0;Assert.Null(ControllerInputBattery.Decode(0x057E,0x2009,report,49));
    }
}
