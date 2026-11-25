using Xunit;
namespace LGSTrayUI.Tests;
public class Rings12SettingsTests
{
    [Fact]public void FieldEditsNotifyOnlyWhenValueChanges(){
        int changes=0;double actual=0;
        var vm=new RingSettingsViewModel(()=>changes++);
        var field=vm.Field(0d,v=>actual=v,v=>RingSettingsViewModel.NormalizeNumber(v,0,10,1));
        field.Value=7.2;field.Value=7.1;
        Assert.Equal(7,actual);Assert.Equal(1,changes);
    }
    [Fact]public void NumericEditorRejectsNonfiniteAndClamps(){
        Assert.False(RingSettingsViewModel.TryNumber("NaN",0,10,1,out _));
        Assert.True(RingSettingsViewModel.TryNumber("99",0,10,1,out var n));Assert.Equal(10,n);
    }
}
