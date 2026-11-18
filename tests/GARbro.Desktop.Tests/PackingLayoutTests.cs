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

public sealed class PackingLayoutTests
{
    [AvaloniaFact]
    public async Task FourLanguagePackingLayoutsRenderWithTheActionInsideTheWindow()
    {
        string snapshots=Environment.GetEnvironmentVariable("GARBRO_PACKING_SCREENSHOTS");
        if(snapshots!=null)Directory.CreateDirectory(snapshots);
        var owner=new AppWindow();owner.Show();
        try
        {
            var catalog=await UiPlatform.Worker.ExecuteAsync<ResourceFormatInfo[]>(new("resource-catalog",null),new WorkerLimits());
            foreach(string language in new[]{"en-US","zh-CN","ru-RU","ja-JP"})
                foreach(string theme in new[]{"light","dark"})
                {
                    MaterialTheme.Apply(new AppSettings {Theme=theme,DynamicColors=false});var text=new DesktopText(language);
                    var pending=Dialogs.Creation(owner,text,catalog,"/Games/中文 Русский 日本語/assets",3,true);
                    var host=Assert.Single(owner.DesktopHost.OwnedWindows);
                    var controls=host.GetVisualDescendants().ToArray();var archives=controls.OfType<ComboBox>().Single(c=>c.Name=="ArchiveOutputFormat");archives.SelectedItem=archives.Items.OfType<ResourceFormatInfo>().Single(f=>f.Tag=="XP3");
                    foreach(var pair in new[]{("ImageOutputFormat",catalog.Single(f=>f.Tag=="WCG"&&f.Type=="image").Id),("AudioOutputFormat","WAV"),("VideoOutputFormat","MP4"),("ScriptOutputFormat","GSC")})
                    {
                        var picker=controls.OfType<ComboBox>().Single(c=>c.Name==pair.Item1);picker.SelectedItem=picker.Items.OfType<UiChoice>().Single(c=>c.Value==pair.Item2);
                    }
                    controls.OfType<CheckBox>().Single(c=>c.Name=="PackingDiscardOriginalMedia").IsChecked=true;
                    Dialogs.SetPath(controls.OfType<TextBox>().Single(c=>c.Name=="ArchiveOutputPath"),"/Games/output/data.xp3");
                    await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);await Task.Delay(80,TestContext.Current.CancellationToken);
                    var action=controls.OfType<Button>().Single(c=>c.Content as string==text["Create"]);var position=action.TranslatePoint(new Point(),host).Value;
                    Assert.True(position.X>=0&&position.Y>=0&&position.X+action.Bounds.Width<=host.ClientSize.Width+1&&position.Y+action.Bounds.Height<=host.ClientSize.Height+1);
                    using var frame=host.CaptureRenderedFrame();Assert.NotNull(frame);Assert.True(frame.PixelSize.Width>400);
                    if(snapshots!=null)frame.Save(Path.Combine(snapshots,$"packing-{language}-{theme}.png"),new PngBitmapEncoderOptions());
                    host.Close();await pending;
                }
        }
        finally {owner.Close();MaterialTheme.Apply(new AppSettings());}
    }
}
