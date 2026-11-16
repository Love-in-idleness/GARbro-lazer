using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GARbro.Application;
using GARbro.Desktop;
using Xunit;

public sealed class ScriptConversionLayoutTests
{
    [AvaloniaFact]
    public async Task ScriptTargetsRenderInFourLanguagesAndRespectTheCheckbox()
    {
        string snapshots=Environment.GetEnvironmentVariable("GARBRO_SCRIPT_SCREENSHOTS");
        if(snapshots!=null)Directory.CreateDirectory(snapshots);
        var owner=new AppWindow();owner.Show();
        try
        {
            foreach(string language in new[]{"en-US","zh-CN","ru-RU","ja-JP"})
            {
                MaterialTheme.Apply(new AppSettings {Theme="dark",DynamicColors=false});var text=new DesktopText(language);
                var pending=Dialogs.Conversion(owner,text,Array.Empty<ResourceFormatInfo>(),"/Games/output",1,true);
                var host=Assert.Single(owner.DesktopHost.OwnedWindows);
                var controls=host.GetVisualDescendants().ToArray();
                var scripts=controls.OfType<CheckBox>().Single(c=>c.Name=="ConvertScripts");
                var target=controls.OfType<ComboBox>().Single(c=>c.Name=="ScriptOutputFormat");
                Assert.Equal(7,target.Items.Count);Assert.Equal("",Assert.IsType<UiChoice>(target.SelectedItem).Value);
                target.SelectedItem=target.Items.OfType<UiChoice>().Single(c=>c.Value=="GSC");
                scripts.IsChecked=false;Assert.False(target.IsEnabled);scripts.IsChecked=true;Assert.True(target.IsEnabled);
                await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);await Task.Delay(60);
                var action=controls.OfType<Button>().Single(c=>c.Content as string==text["Start"]);var at=action.TranslatePoint(new Point(),host).Value;
                Assert.True(at.Y>=0 && at.Y+action.Bounds.Height<=host.ClientSize.Height+1);
                using var frame=host.CaptureRenderedFrame();Assert.NotNull(frame);
                if(snapshots!=null)frame.Save(Path.Combine(snapshots,$"scripts-{language}.png"),new PngBitmapEncoderOptions());
                host.Close();await pending;
            }
        }
        finally {owner.Close();MaterialTheme.Apply(new AppSettings());}
    }
}
