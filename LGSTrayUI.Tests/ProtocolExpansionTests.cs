using Xunit;
namespace LGSTrayUI.Tests;
public class ProtocolExpansionTests{
    [Fact]public void AstroMatchesOnlySolicitedBatteryReport(){
        byte[] reply=[2,0x0C,6,0,6,42,72,72,1];
        Assert.Equal((72,true),AsusBatteryReader.DecodeAstro(reply,reply.Length,42)!.Value);
        Assert.Null(AsusBatteryReader.DecodeAstro(reply,reply.Length,43));
        Assert.Null(AsusBatteryReader.DecodeAstro(reply,8,42));
        reply[6]=255;Assert.Null(AsusBatteryReader.DecodeAstro(reply,reply.Length,42));
        Assert.Equal(64,AsusBatteryReader.AstroRequest(42).Length);
    }
    [Fact]public void DirectKeyboardUsesCorrectOffset(){
        byte[] frame=[0,0x12,1,0,0,0,6,0,0,1,0,72];
        Assert.Equal((72,true),AsusBatteryReader.DecodeDirectKeyboard(frame,11)!.Value);
        Assert.Equal((6,true),AsusBatteryReader.DecodeDirectKeyboard(frame[1..],6)!.Value);
        Assert.Null(AsusBatteryReader.DecodeDirectKeyboard(frame[..8],11));
    }
    [Fact]public void DirectHeadsetRequiresBothResponses(){
        byte[] battery=[0xCC,0x12,7,0,0,0,83];
        byte[] charge=[0xCC,0x12,8,0,0,1];
        Assert.Equal((83,true),AsusBatteryReader.DecodeDirectHeadset(battery,charge)!.Value);
        Assert.Null(AsusBatteryReader.DecodeDirectHeadset(battery,null));
        charge[2]=7;Assert.Null(AsusBatteryReader.DecodeDirectHeadset(battery,charge));
    }
}
