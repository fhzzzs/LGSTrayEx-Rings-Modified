using Xunit;
namespace LGSTrayUI.Tests;
public class ThemeTileTests
{
    [Theory]
    [InlineData(480,4,1)]
    [InlineData(768,4,2)]
    [InlineData(900,4,2)]
    [InlineData(1100,4,4)]
    [InlineData(900,3,3)]
    [InlineData(1400,1,1)]
    public void GridAdaptsToAvailableWidth(double width,int count,int expected)
        =>Assert.Equal(expected,ThemeTileGeometry.Columns(width,count));
    [Theory]
    [InlineData(768,2)]
    [InlineData(1200,4)]
    [InlineData(768,1)]
    public void RowFillsAvailableWidth(double width,int cells)
        =>Assert.Equal(width,ThemeTileGeometry.CellWidth(width,cells)*cells+ThemeTileGeometry.Gap*(cells-1),6);
}
