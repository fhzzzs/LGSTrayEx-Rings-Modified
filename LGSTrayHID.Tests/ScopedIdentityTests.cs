using LGSTrayHID.Metadata;
using Xunit;
namespace LGSTrayHID.Tests;
public class ScopedIdentityTests{
    [Fact]public void SameNameDevicesAreDistinctAcrossReceiversAndSlots(){
        var a=DeviceIdentifierGenerator.GenerateScopedFallback("Mouse","receiver1",1);
        Assert.Equal(a,DeviceIdentifierGenerator.GenerateScopedFallback("Mouse","receiver1",1));
        Assert.NotEqual(a,DeviceIdentifierGenerator.GenerateScopedFallback("Mouse","receiver2",1));
        Assert.NotEqual(a,DeviceIdentifierGenerator.GenerateScopedFallback("Mouse","receiver1",2));
    }
}
