using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GARbro.Desktop;
using Xunit;

public sealed class MaterialTests
{
    [Theory]
    [InlineData(1180,780,1200,760,1180,720)] // 1800x1200 screen at 150%, excluding taskbar
    [InlineData(2400,1800,1024,728,1008,688)] // oversized saved preferences
    [InlineData(1180,780,640,400,624,360)] // minimum size must also adapt
    public void RestoredWindowLeavesRoomForItsFrame(double width,double height,double workWidth,double workHeight,double expectedWidth,double expectedHeight)
    {
        var fit=GARbro.Domain.WindowGeometry.Fit(width,height,850,540,workWidth,workHeight,16,40);
        Assert.Equal(expectedWidth,fit.Width);Assert.Equal(expectedHeight,fit.Height);
        Assert.True(fit.MinimumWidth<=fit.Width);Assert.True(fit.MinimumHeight<=fit.Height);
    }
    [Theory]
    [InlineData(-120,-1080,1080,1000,-1000)]
    [InlineData(-50,0,1920,1200,0)]
    [InlineData(400,0,640,800,0)]
    public void WindowOriginKeepsTheCaptionInsideNegativeAndSmallDisplays(int position,int origin,int available,int size,int expected)
        => Assert.Equal(expected,GARbro.Domain.WindowGeometry.ConstrainOrigin(position,origin,available,size));
    [Fact]
    public void DiagnosticTranslationPreservesResourceNamesAndOriginalEnglish()
    {
        const string message = "Output exists: 中文 archive.xfl";
        Assert.Equal("输出文件已存在：中文 archive.xfl", new DesktopText("zh-CN").ErrorMessage(message));
        Assert.Equal(message, new DesktopText("en-US").ErrorMessage(message));
        Assert.Equal("第三方诊断", new DesktopText("zh-CN").ErrorMessage("第三方诊断"));
    }
    [Fact]
    public void DiagnosticRowsAcceptWindowsLineEndingsWithoutChangingWhitespace()
    {
        Assert.Equal("输出： sample", GARbro.Domain.MessageTranslation.Translate("Output:  sample", "Output: |输出：\r\n", "zh-CN"));
    }
    [Fact]
    public void VersionOnePreferencesMigrateWithoutHidingTheOnlyMenu()
    {
        string file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try
        {
            File.WriteAllText(file,"{\"Version\":1,\"MenuVisible\":false,\"Language\":\"zh-CN\",\"TextEncoding\":\"gbk\"}");
            var settings=new SettingsStore(file).Load();Assert.Equal(3,settings.Version);Assert.True(settings.MenuVisible);
            Assert.Equal("gbk",settings.TextEncoding);Assert.Equal("zh-CN",settings.Language);Assert.Equal("#6750A4",settings.SeedColor);
        }
        finally{File.Delete(file);}
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaterialRolesHaveReadableTextAndRespondToTheSeed(bool dark)
    {
        var first=MaterialTheme.Palette(new AppSettings{SeedColor="#6750A4",DynamicColors=false},dark);
        var second=MaterialTheme.Palette(new AppSettings{SeedColor="#008577",DynamicColors=false},dark);
        Assert.NotEqual(first["Primary"],second["Primary"]);
        Assert.True(Contrast(first["Surface"],first["OnSurface"])>=4.5);
        Assert.True(Contrast(first["PrimaryContainer"],first["OnPrimaryContainer"])>=4.5);
    }
    static double Contrast(uint a,uint b)
    {
        double L(uint color){double C(int shift){double c=((color>>shift)&255)/255d;return c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);}return .2126*C(16)+.7152*C(8)+.0722*C(0);}
        double x=L(a),y=L(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
    }
    [AvaloniaFact]
    public void DesktopPagesKeepFullWindowControlsOutsideTheApplicationHeader()
    {
        var page=new MainWindow();var host=page.DesktopHost;
        Assert.Equal(WindowDecorations.Full,host.WindowDecorations);
        Assert.Equal(!OperatingSystem.IsMacOS(),host.ExtendClientAreaToDecorationsHint);
        Assert.NotNull(host.WindowDecorationsTheme);
        Assert.True(host.CanMinimize);Assert.True(host.CanMaximize);Assert.True(host.CanResize);
        host.Close();
    }
    [AvaloniaFact]
    public async Task MobileDialogUsesPagesAndCanReturnItsResultWithoutCreatingAWindow()
    {
        bool mobile=UiPlatform.IsMobile;var previous=UiPlatform.Navigation;
        try
        {
            UiPlatform.IsMobile=true;var root=new AppWindow();UiPlatform.Navigation=new PageNavigation(root);
            var page=new AppWindow();var result=page.ShowDialog<string>(root);
            Assert.Equal(2,UiPlatform.Navigation.Children.Count);Assert.False(root.IsVisible);
            Assert.Throws<PlatformNotSupportedException>(()=>page.DesktopHost);
            page.Close("saved");Assert.Equal("saved",await result);Assert.True(root.IsVisible);Assert.Single(UiPlatform.Navigation.Children);
        }
        finally{UiPlatform.Navigation=previous;UiPlatform.IsMobile=mobile;}
    }
}
