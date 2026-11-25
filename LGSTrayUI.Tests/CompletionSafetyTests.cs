using LGSTrayPrimitives;
using Xunit;
namespace LGSTrayUI.Tests;
public class CompletionSafetyTests{
    [Fact]public void DisplayCatalogValidatesTypesAndDuplicateNames(){
        var c=PeripheralDisplayCatalog.Parse("{\"schema\":1,\"models\":[{\"name\":\"Test Mouse\",\"type\":\"mouse\"}]}");
        Assert.Equal(DeviceType.Mouse,c["test mouse"]);
        Assert.Throws<System.FormatException>(()=>PeripheralDisplayCatalog.Parse("{\"schema\":1,\"models\":[{\"name\":\"Test Mouse\",\"type\":\"firmware\"}]}"));
        Assert.Throws<System.FormatException>(()=>PeripheralDisplayCatalog.Parse("{\"schema\":1,\"models\":[{\"name\":\"Test Mouse\",\"type\":\"mouse\"},{\"name\":\"test mouse\",\"type\":\"keyboard\"}]}"));
    }
    [Fact]public void FailedShellProbeBackoffIsBounded(){
        Assert.Equal(0,TaskbarAutomationProbe.RetryDelay(0));
        Assert.True(TaskbarAutomationProbe.RetryDelay(1)>0);
        Assert.InRange(TaskbarAutomationProbe.RetryDelay(100),1,5000);
    }
}
