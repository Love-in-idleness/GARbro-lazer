using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GARbro.Application;
using GARbro.Desktop;

internal static class LocalizationSnapshots
{
    internal static async Task Check(MainWindow page)
    {
        if (Environment.GetEnvironmentVariable("GARBRO_LOCALIZATION_SNAPSHOTS") != "1") return;
        string directory=Path.Combine(Program.Output,"localization");Directory.CreateDirectory(directory);
        string demo=Path.Combine(Program.Output,"demo");Directory.CreateDirectory(demo);
        foreach(var (source,target)in new[]{("movie.mpg","Movie.mpg"),("tone.wav","Music.wav"),("standard.xfl","Resources.xfl"),("colors-rgb.png","Scene.png")})
            File.Copy(Path.Combine(Path.GetDirectoryName(Program.Fixture),source),Path.Combine(demo,target),true);
        File.WriteAllText(Path.Combine(demo,"Notes.txt"),"GARbro — game resource browser\n");
        double width=page.Width,height=page.Height;page.Width=1440;page.Height=810;
        try
        {
            await page.LoadInputsAsync(new[]{demo});
            foreach(string language in new[]{"en-US","zh-CN","ru-RU","ja-JP"})
                foreach(string theme in new[]{"light","dark"})
                {
                    CaptionVisuals.Appearance(page,theme,language);
                    var list=page.GetVisualDescendants().OfType<ListBox>().Single(b=>b.Name=="Files");
                    list.SelectedItem=list.Items.OfType<BrowserEntry>().Single(e=>e.Name=="Movie.mpg");
                    for(int n=0;n<100;n++)
                    {
                        await Task.Delay(30);
                        if(page.GetVisualDescendants().OfType<Image>().Any(i=>i.Name=="PreviewImage" && i.Source!=null))break;
                    }
                    await Ready();Capture(page.DesktopHost,Path.Combine(directory,$"desktop-{language}-{theme}.png"));
                    var settings=new AppSettings {Language=language,Theme=theme};
                    var dialog=new SettingsWindow(settings,new DesktopText(language),Array.Empty<ResourceFormatInfo>(),"Shortcuts");
                    var completion=dialog.ShowDialog<bool>(page);await Ready();
                    Capture(dialog.DesktopHost,Path.Combine(directory,$"shortcuts-{language}-{theme}.png"));
                    dialog.Close(false);await completion;
                    var extraction=Dialogs.Extraction(page,new DesktopText(language),Program.Output,true,2);await Ready();
                    var extractHost=page.DesktopHost.OwnedWindows.Single();
                    var controls=extractHost.GetVisualDescendants().ToArray();
                    controls.OfType<CheckBox>().Single(c=>c.Name=="RecursiveExtraction").IsChecked=true;
                    foreach(var pair in new[]{("AudioOutputFormat","WAV"),("VideoOutputFormat","MP4")})
                    {
                        var picker=controls.OfType<ComboBox>().Single(c=>c.Name==pair.Item1);picker.SelectedItem=picker.Items.OfType<UiChoice>().Single(c=>c.Value==pair.Item2);
                    }
                    await Ready();Capture(extractHost,Path.Combine(directory,$"extraction-{language}-{theme}.png"));extractHost.Close();await extraction;
                }
        }
        finally{page.Width=width;page.Height=height;CaptionVisuals.Appearance(page,"system","auto",true);await page.LoadArchiveAsync(Program.Fixture);}
    }
    static async Task Ready(){await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);await Task.Delay(120);}
    static void Capture(Window window,string path)
    {
        var root=window.GetVisualAncestors().LastOrDefault()??window;
        using var bitmap=new RenderTargetBitmap(PixelSize.FromSize(root.Bounds.Size,window.RenderScaling),new Vector(96*window.RenderScaling,96*window.RenderScaling));
        bitmap.Render(root);bitmap.Save(path,new PngBitmapEncoderOptions());
    }
}
