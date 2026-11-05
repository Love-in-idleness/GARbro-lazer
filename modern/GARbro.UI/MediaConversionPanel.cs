using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using GARbro.Application;

namespace GARbro.Desktop;

/// The same explicit image/audio/video choices are used for extraction and batch conversion.
public sealed class MediaConversionPanel : StackPanel
{
    readonly ComboBox image,audio,video;
    public event Action Changed;
    static string Value(ComboBox selector) => (selector.SelectedItem as UiChoice)?.Value is {Length:>0} value?value:null;
    public string ImageFormat => Value(image);
    public string AudioFormat => Value(audio);
    public string VideoFormat => Value(video);
    public bool HasConversion => ImageFormat!=null || AudioFormat!=null || VideoFormat!=null;
    public MediaConversionPanel(DesktopText text,ResourceFormatInfo[] imageFormats=null,bool batch=false)
    {
        Spacing=10;var columns=new Grid {ColumnDefinitions=new("*,*,*"),ColumnSpacing=12,RowSpacing=8};Children.Add(columns);
        ComboBox Selector(string name,string label,UiChoice[] choices,int initial)
        {
            var field=new StackPanel {Spacing=6};field.Children.Add(Dialogs.Label(text,label));
            Grid.SetColumn(field,columns.Children.Count);columns.Children.Add(field);
            var selector=new ComboBox {Name=name,ItemsSource=choices,SelectedIndex=initial,HorizontalAlignment=HorizontalAlignment.Stretch,
                ItemTemplate=new FuncDataTemplate<UiChoice>((choice,_)=>new TextBlock {Text=choice?.Label,TextWrapping=TextWrapping.Wrap,MaxWidth=490})};
            selector.SelectionChanged+=(_,_)=>Changed?.Invoke();field.Children.Add(selector);return selector;
        }
        var original=new UiChoice("",text["KeepOriginalFormat"]);
        var images=imageFormats==null?new[]{"PNG","BMP","JPEG","TIFF","TGA"}.Select(f=>new UiChoice(f,f)):
            imageFormats.Select(f=>new UiChoice(f.Id,f.Tag));
        image=Selector("ImageOutputFormat","ImageFormat",new[]{original}.Concat(images).ToArray(),batch?1:0);
        audio=Selector("AudioOutputFormat","AudioFormat",new[]{original,new UiChoice("AUTO",text["AudioAuto"]),new UiChoice("WAV","WAV"),new UiChoice("FLAC","FLAC"),new UiChoice("M4A","M4A (AAC)")},batch?1:0);
        video=Selector("VideoOutputFormat","VideoFormat",new[]{original,new UiChoice("MP4","MP4 (MPEG-4/AAC)"),new UiChoice("MKV",text["VideoMkv"])},batch?1:0);
        SizeChanged+=(_,_)=>
        {
            bool narrow=Bounds.Width<540;columns.ColumnDefinitions=new(narrow?"*":"*,*,*");columns.RowDefinitions=new(narrow?"Auto,Auto,Auto":"Auto");
            for(int i=0;i<columns.Children.Count;i++){Grid.SetColumn(columns.Children[i],narrow?0:i);Grid.SetRow(columns.Children[i],narrow?i:0);}
        };
        Children.Add(new TextBlock {Text=text["MediaConversionHint"],TextWrapping=TextWrapping.Wrap,Classes={"muted"}});
    }
}
