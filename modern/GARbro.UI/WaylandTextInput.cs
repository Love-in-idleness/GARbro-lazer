using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace GARbro.Desktop
{
    /// <summary>Negotiate cached text-input-v3 state after a new editor gains focus.</summary>
    internal static class WaylandTextInput
    {
        static readonly ConditionalWeakTable<TextInputMethodClient,Client> clients=new();

        public static void Attach(AppWindow page) { /* Attached by the native desktop host. */ }

        public static void Attach(Window window)
        {
            if(!OperatingSystem.IsLinux())return;
            TextInputMethodClient previous=null;
            window.Deactivated+=(_,_)=>previous=null;
            window.AddHandler(InputElement.TextInputMethodClientRequestedEvent,(_,args)=>
            {
                if(window.PlatformImpl?.GetType().Assembly.GetName().Name!="Avalonia.Wayland")return;
                var original=args.Client;
                if(original==null){previous=null;return;}
                var client=clients.GetValue(original,value=>new Client(value));
                args.Client=client;
                if(previous!=original)
                {
                    previous=original;
                    // Avalonia 12.1.4 enables v3 before pushing the initial
                    // surrounding text. wlroots captures supported features at
                    // enable+commit. Re-enable through the public reset event
                    // once the normal initialization has populated that state.
                    Dispatcher.UIThread.Post(client.Reinitialize,DispatcherPriority.Input);
                }
            },RoutingStrategies.Bubble,handledEventsToo:true);
        }

        sealed class Client : TextInputMethodClient
        {
            readonly TextInputMethodClient original;
            public Client(TextInputMethodClient original)
            {
                this.original=original;
                original.TextViewVisualChanged+=(_,_)=>RaiseTextViewVisualChanged();
                original.CursorRectangleChanged+=(_,_)=>RaiseCursorRectangleChanged();
                original.SurroundingTextChanged+=(_,_)=>RaiseSurroundingTextChanged();
                original.SelectionChanged+=(_,_)=>RaiseSelectionChanged();
                original.ResetRequested+=(_,_)=>RequestReset();
                original.InputPaneActivationRequested+=(_,_)=>RaiseInputPaneActivationRequested();
            }
            public void Reinitialize()=>RequestReset();
            public override Visual TextViewVisual=>original.TextViewVisual;
            public override bool SupportsPreedit=>original.SupportsPreedit;
            public override bool SupportsSurroundingText=>original.SupportsSurroundingText;
            public override string SurroundingText=>original.SurroundingText;
            public override Rect CursorRectangle=>original.CursorRectangle;
            public override TextSelection Selection {get=>original.Selection;set=>original.Selection=value;}
            public override void SetPreeditText(string value)=>original.SetPreeditText(value);
            public override void SetPreeditText(string value,int? cursor)=>original.SetPreeditText(value,cursor);
            public override void ExecuteContextMenuAction(ContextMenuAction action)=>original.ExecuteContextMenuAction(action);
        }
    }
}
