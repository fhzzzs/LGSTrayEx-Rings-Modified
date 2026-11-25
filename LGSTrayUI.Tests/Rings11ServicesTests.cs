using System;
using Xunit;
namespace LGSTrayUI.Tests;
public class Rings11ServicesTests
{
    [Fact]public void ColorChangeDoesNotInvalidateIdentityOrProviders(){
        var c=new RingPreferences();var before=PreferenceFingerprint.Capture(c);c.Normal="#123456";
        Assert.Equal(PreferenceArea.Presentation,before.Diff(PreferenceFingerprint.Capture(c)));
    }
    [Fact]public void ConnectionDuplicatesAreSuppressedButOppositeTransitionIsNot(){
        var now=DateTimeOffset.UnixEpoch;
        Assert.True(DeviceNotificationCenter.IsDuplicate((true,now),true,now.AddSeconds(1)));
        Assert.False(DeviceNotificationCenter.IsDuplicate((true,now),false,now.AddSeconds(1)));
        Assert.False(DeviceNotificationCenter.IsDuplicate((true,now),true,now.AddSeconds(6)));
    }
}
