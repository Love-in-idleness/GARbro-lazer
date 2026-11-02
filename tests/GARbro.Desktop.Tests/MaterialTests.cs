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
            var settings=new SettingsStore(file).Load();Assert.Equal(2,settings.Version);Assert.True(settings.MenuVisible);
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
        Assert.False(host.ExtendClientAreaToDecorationsHint);
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
