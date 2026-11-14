using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using GARbro.Desktop;
using Xunit;

public sealed class CaptionTests
{
    [AvaloniaFact]
    public async Task LanguageSwitchUpdatesExistingIndexerBindings()
    {
        var language=new DesktopText("en-US");
        var label=new TextBlock();label.Bind(TextBlock.TextProperty,new Avalonia.Data.Binding("[Settings]"){Source=language});
        var window=new Window{Content=label};
        try
        {
            window.Show();await Ready();Assert.Equal("Settings",label.Text);
            language.Change("zh-CN");await Ready();Assert.Equal("设置",label.Text);
            language.Change("en-US");await Ready();Assert.Equal("Settings",label.Text);
        }
        finally {window.Close();}
    }
    [AvaloniaTheory]
    [InlineData("light",1)]
    [InlineData("dark",1)]
    [InlineData("light",1.25)]
    [InlineData("dark",1.25)]
    [InlineData("light",1.5)]
    [InlineData("dark",1.5)]
    [InlineData("light",2)]
    [InlineData("dark",2)]
    public async Task CaptionStatesPaintCirclesInsideTheUnchangedHitArea(string theme,double scale)
    {
        MaterialTheme.Apply(new AppSettings {Theme=theme,DynamicColors=false});
        var app=Avalonia.Application.Current;
        var buttons=new[]{"GarbroCaptionButton","GarbroCaptionButton","GarbroCloseCaptionButton"}.Select(key=>
        {
            Assert.True(app.TryFindResource(key,out var resource));
            return new Button {Theme=(ControlTheme)resource,Classes={"captionButton"}};
        }).ToArray();
        var row=new StackPanel {Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=2};
        foreach(var button in buttons)row.Children.Add(button);
        var focusSink=new TextBox {Width=80,Height=24};
        var content=new Canvas();content.Children.Add(row);content.Children.Add(focusSink);
        Canvas.SetLeft(row,12);Canvas.SetTop(row,12);Canvas.SetLeft(focusSink,12);Canvas.SetTop(focusSink,70);
        var window=new Window {Width=200,Height=120,WindowDecorations=WindowDecorations.None,Content=content};
        try
        {
            window.Show();window.SetRenderScaling(scale);
            await Ready();
            foreach(var button in buttons)
            {
                window.MouseMove(new Point(190,110));focusSink.Focus(NavigationMethod.Pointer);
                await Ready();
                Assert.Equal(46,button.Width);Assert.Equal(40,button.Height);
                Assert.InRange(button.Bounds.Width,46-1/scale,46+1/scale);
                Assert.InRange(button.Bounds.Height,40-1/scale,40+1/scale);
                Point origin=button.TranslatePoint(default,window).Value,center=origin+new Vector(23,20);
                var corners=new[]{new Vector(-14,-14),new Vector(14,-14),new Vector(-14,14),new Vector(14,14)};
                using var idle=window.CaptureRenderedFrame();Assert.NotNull(idle);
                var background=Pixel(idle,center,scale);

                // The outer rectangle is still clickable; its corners must not
                // acquire a rectangular hover/pressed background.
                Point hit=origin+new Vector(1,1);window.MouseMove(hit);await Ready();
                Assert.True(button.IsPointerOver);
                using var hover=window.CaptureRenderedFrame();
                Assert.True(Difference(background,Pixel(hover,center,scale))>5);
                foreach(var corner in corners)Assert.True(Difference(Pixel(idle,center+corner,scale),Pixel(hover,center+corner,scale))<=2);

                window.MouseDown(hit,MouseButton.Left);await Ready();
                Assert.True(button.IsPressed);
                using var pressed=window.CaptureRenderedFrame();
                Assert.True(Difference(Pixel(hover,center,scale),Pixel(pressed,center,scale))>3);
                foreach(var corner in corners)Assert.True(Difference(Pixel(idle,center+corner,scale),Pixel(pressed,center+corner,scale))<=2);
                window.MouseUp(new Point(190,110),MouseButton.Left);

                button.IsEnabled=false;window.MouseMove(hit);await Ready();
                using var disabled=window.CaptureRenderedFrame();
                Assert.True(Difference(background,Pixel(disabled,center,scale))<=2);

                button.IsEnabled=true;window.MouseMove(new Point(190,110));button.Focus(NavigationMethod.Tab);await Ready();
                Assert.True(button.IsFocused);
                using var focused=window.CaptureRenderedFrame();
                Assert.True(Difference(Pixel(idle,center+new Vector(0,-15),scale),Pixel(focused,center+new Vector(0,-15),scale))>5);
                foreach(var corner in corners)Assert.True(Difference(Pixel(idle,center+corner,scale),Pixel(focused,center+corner,scale))<=2);
            }
        }
        finally {window.Close();}
    }
    static async Task Ready()
    {
        await Dispatcher.UIThread.InvokeAsync(()=>{},DispatcherPriority.Background);
        await Task.Delay(30,TestContext.Current.CancellationToken);
    }
    static (int R,int G,int B) Pixel(Bitmap frame,Point logical,double scale)
    {
        int stride=frame.PixelSize.Width*4;var bytes=new byte[stride*frame.PixelSize.Height];var pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);
        try {frame.CopyPixels(new PixelRect(frame.PixelSize),pinned.AddrOfPinnedObject(),bytes.Length,stride);}
        finally {pinned.Free();}
        int at=(int)(logical.Y*scale)*stride+(int)(logical.X*scale)*4;
        return frame.Format==Avalonia.Platform.PixelFormat.Rgba8888?(bytes[at],bytes[at+1],bytes[at+2]):(bytes[at+2],bytes[at+1],bytes[at]);
    }
    static int Difference((int R,int G,int B)a,(int R,int G,int B)b)=>Math.Max(Math.Abs(a.R-b.R),Math.Max(Math.Abs(a.G-b.G),Math.Abs(a.B-b.B)));
}
