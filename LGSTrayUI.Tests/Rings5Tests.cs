using Xunit;
namespace LGSTrayUI.Tests;
public class Rings5Tests
{
    [Fact] public void DragCrossesNeighborMidpointInBothDirections()
    {
        double[] centers=[50,150,250];
        Assert.Equal(1,LGSTrayUI.DeviceDragReorder.TargetIndex(0,110,centers));
        Assert.Equal(0,LGSTrayUI.DeviceDragReorder.TargetIndex(1,90,centers));
        Assert.Equal(2,LGSTrayUI.DeviceDragReorder.TargetIndex(0,240,centers));
    }
    [Fact] public void ChargingHighlightStaysOnFilledArc()
    {
        foreach(double phase in new[]{0,.1,.5,.9,1}){
            var (start,end)=LGSTrayUI.BatteryRing.ShineSegment(phase,65,15);
            Assert.InRange(start,0,234);Assert.InRange(end,start,234);
        }
    }
    [Fact] public void OmniReaderValidatesModelAndReply()
    {
        byte[] pairs=new byte[64];pairs[0]=1;pairs[1]=0xA0;pairs[5]=0x65;pairs[6]=0x1B;
        Assert.True(LGSTrayUI.AsusBatteryReader.PairedHarpe(pairs));pairs[5]=0;Assert.False(LGSTrayUI.AsusBatteryReader.PairedHarpe(pairs));
        byte[] response=new byte[64];response[0]=3;response[1]=0x12;response[2]=7;response[5]=65;response[10]=1;
        Assert.Equal((65,true),LGSTrayUI.AsusBatteryReader.Decode(response)!.Value);
        response[5]=101;Assert.Null(LGSTrayUI.AsusBatteryReader.Decode(response));response[5]=0;response[10]=0;Assert.Null(LGSTrayUI.AsusBatteryReader.Decode(response));
    }
    [Fact] public void BluetoothNamesRecognizeRequestedModels()
    {
        Assert.Equal(LGSTrayPrimitives.DeviceType.Headset,LGSTrayUI.BluetoothBatteryReader.Classify("G735",0,0));
        Assert.Equal(LGSTrayPrimitives.DeviceType.Mouse,LGSTrayUI.BluetoothBatteryReader.Classify("ROG Harpe Ace Mini",0,0));
    }
}
