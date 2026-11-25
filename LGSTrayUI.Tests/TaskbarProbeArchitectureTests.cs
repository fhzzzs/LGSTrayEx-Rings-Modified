using Xunit;
namespace LGSTrayUI.Tests;
public class TaskbarProbeArchitectureTests
{
    [Fact]public void GeometryMustBeFreshAndCompleted()
    {
        Assert.True(TaskbarAutomationProbe.Fresh(10000,9900,9950));
        Assert.False(TaskbarAutomationProbe.Fresh(10000,8000,9950));
        Assert.False(TaskbarAutomationProbe.Fresh(10000,9900,10001));
        Assert.False(TaskbarAutomationProbe.Fresh(10000,9900,9800));
    }
}
