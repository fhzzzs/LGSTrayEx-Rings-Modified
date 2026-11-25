using LGSTrayHID.Features;
using LGSTrayHID.Protocol;
using Xunit;
namespace LGSTrayHID.Tests;
public class BatteryFeatureSelectionTests{
    [Fact]public void PreferDirectReadingBeforeVoltage(){
        var candidates=BatteryFeatureFactory.GetCandidates(new(){[HidppFeature.BATTERY_VOLTAGE]=1,[HidppFeature.BATTERY_UNIFIED]=2,[HidppFeature.BATTERY_UNIFIED_LEVEL]=3});
        Assert.Equal(new[]{HidppFeature.BATTERY_UNIFIED_LEVEL,HidppFeature.BATTERY_UNIFIED,HidppFeature.BATTERY_VOLTAGE},candidates.Select(x=>x.FeatureId));
    }
    [Fact]public void TruncatedAndInvalidEventsAreRejected(){
        Assert.Null(new BatteryUnifiedLevel().ParseBatteryEvent(new Hidpp20([0x11,1,2,0])));
        Assert.Null(new BatteryUnifiedLevel().ParseBatteryEvent(new Hidpp20([0x11,1,2,0,255,0,0])));
        Assert.Null(new BatteryVoltage().ParseBatteryEvent(new Hidpp20([0x11,1,2,0,0,0,0])));
    }
}
