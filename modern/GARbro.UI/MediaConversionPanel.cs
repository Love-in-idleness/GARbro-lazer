using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using GARbro.Application;
using GARbro.Domain;

namespace GARbro.Desktop;

/// The same explicit image/audio/video choices are used for extraction and batch conversion.
public sealed class MediaConversionPanel : StackPanel
{
    readonly ComboBox image,audio,video;
    readonly ComboBox scriptOutput;
    public event Action Changed;
    static string Value(ComboBox selector) => (selector?.SelectedItem as UiChoice)?.Value is {Length:>0} value?value:null;
    public string ImageFormat => Value(image);
    public string AudioFormat => Value(audio);
    public string VideoFormat => Value(video);
    public bool ConvertScripts => Value(scriptOutput)!=null;
    public string ScriptFormat => Value(scriptOutput) is {} value && value!="EXPORT"?value:null;
    public bool HasConversion => ImageFormat!=null || AudioFormat!=null || VideoFormat!=null || ConvertScripts;
    public MediaConversionPanel(DesktopText text,ResourceFormatInfo[] imageFormats=null,bool batch=false,bool offerScripts=false)
    {
        Spacing=10;var columns=new Grid {ColumnDefinitions=new(offerScripts?"*,*,*,*":"*,*,*"),ColumnSpacing=12,RowSpacing=8};Children.Add(columns);Grid.SetIsSharedSizeScope(columns,true);
        ComboBox Selector(string name,string label,UiChoice[] choices,int initial)
        {
            var field=new Grid {RowDefinitions=new("Auto,Auto"),RowSpacing=6};field.RowDefinitions[0].SharedSizeGroup="FormatLabels";field.RowDefinitions[1].SharedSizeGroup="FormatControls";field.Children.Add(Dialogs.Label(text,label));
            Grid.SetColumn(field,columns.Children.Count);columns.Children.Add(field);
            var selector=new ComboBox {Name=name,ItemsSource=choices,SelectedIndex=initial,HorizontalAlignment=HorizontalAlignment.Stretch,
                ItemTemplate=new FuncDataTemplate<UiChoice>((choice,_)=>new TextBlock {Text=choice?.Label,TextWrapping=TextWrapping.Wrap,MaxWidth=490})};
            selector.SelectionChanged+=(_,_)=>Changed?.Invoke();Grid.SetRow(selector,1);field.Children.Add(selector);return selector;
        }
        var original=new UiChoice("",text["KeepOriginalFormat"]);
        string ImageLabel(string tag)=>tag=="WEBP"?text["WebpLossless"]:tag;
        var common=ImageOutputFormats.Common;
        var images=imageFormats==null?common.Select(f=>new UiChoice(f,ImageLabel(f))):
            imageFormats.OrderBy(f=>Array.IndexOf(common,f.Tag) is int index && index>=0?index:int.MaxValue).ThenBy(f=>f.Tag,StringComparer.Ordinal)
                .Select(f=>new UiChoice(f.Id,ImageLabel(f.Tag)));
        image=Selector("ImageOutputFormat","ImageFormat",new[]{original}.Concat(images).ToArray(),batch?1:0);
        audio=Selector("AudioOutputFormat","AudioFormat",new[]{original,new UiChoice("AUTO",text["AudioAuto"]),new UiChoice("WAV","WAV"),new UiChoice("FLAC","FLAC"),new UiChoice("M4A","M4A (AAC)")},batch?1:0);
        video=Selector("VideoOutputFormat","VideoFormat",new[]{original,new UiChoice("MP4","MP4 (MPEG-4/AAC)"),new UiChoice("MKV",text["VideoMkv"])},batch?1:0);
        if(offerScripts)
            scriptOutput=Selector("ScriptOutputFormat","ScriptOutputFormat",new[]{new UiChoice("",text["NoScriptConversion"]),new UiChoice("EXPORT",text["ScriptExport"])}
                .Concat(ScriptOutputFormats.Recompile.Select(format=>new UiChoice(format,text["ScriptRecompile"]+" · "+format))).ToArray(),1);
        SizeChanged+=(_,_)=>
        {
            int count=columns.Children.Count;
            int perRow=Bounds.Width<520?1:offerScripts && Bounds.Width<720?2:count;
            columns.ColumnDefinitions=new(string.Join(",",Enumerable.Repeat("*",perRow)));
            columns.RowDefinitions=new(string.Join(",",Enumerable.Repeat("Auto",(count+perRow-1)/perRow)));
            for(int i=0;i<count;i++){Grid.SetColumn(columns.Children[i],i%perRow);Grid.SetRow(columns.Children[i],i/perRow);}
        };
        Children.Add(new TextBlock {Text=text["MediaConversionHint"],TextWrapping=TextWrapping.Wrap,Classes={"muted"}});
        if(offerScripts)Children.Add(new TextBlock {Text=text["ScriptConverterHint"],TextWrapping=TextWrapping.Wrap,Classes={"muted"}});
    }
}
