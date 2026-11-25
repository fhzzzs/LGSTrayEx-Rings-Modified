using Xunit;
namespace LGSTrayUI.Tests;
public class TaskbarProbeArchitectureTests
{
    [Fact]public void GeometryMustBeFreshAndSampledAfterNativeAcknowledgement(){
        Assert.True(TaskbarAutomationProbe.Fresh(10000,9900,9950,9800));
        Assert.False(TaskbarAutomationProbe.Fresh(10000,9700,9950,9800));
        Assert.False(TaskbarAutomationProbe.Fresh(10000,8000,9950,7000));
        Assert.False(TaskbarAutomationProbe.Fresh(10000,9900,10001,9800));
    }
    [Fact]public void StrictNativeModeIsDefault()=>Assert.True(new RingPreferences().RequireNativeLeft);
}
