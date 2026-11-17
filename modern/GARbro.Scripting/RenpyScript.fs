namespace GARbro.Scripting

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Threading
open PickleData
open ScriptSyntax

module internal RenpySource =
    let name = function PyObject o -> o.Name | _ -> invalid "Invalid Ren'Py AST node."
    let str key node = let v=field key node in if present v then text v else ""
    let labelName node = let value=str "name" node in if value="" then str "_name" node else value
    let list key node = values (field key node)
    let optional key prefix node = let s=str key node in if s="" then "" else prefix+s
    let joined separator nodes = nodes |> Array.map text |> String.concat separator
    let code node = let source=field "source" node in if present source then text source else text node
    let number key fallback node = match field key node with PyInteger n -> int n | _ -> fallback
    let isTrue key fallback node = match field key node with PyNone -> fallback | v -> truth v
    let sayString (s:string) = quote s |> fun s -> Regex.Replace(s,@"(?<= ) ","\\ ")

    let arguments node =
        if not (present node) then ""
        else
            let entries=list "arguments" node
            let starred=list "starred_indexes" node |> Array.map text |> Set.ofArray
            let doubled=list "doublestarred_indexes" node |> Array.map text |> Set.ofArray
            let parts=ResizeArray<string>()
            for i in 0..entries.Length-1 do
                match values entries[i] with
                | [|key;value|] ->
                    let prefix=if present key then text key+"=" elif starred.Contains(string i) then "*" elif doubled.Contains(string i) then "**" else ""
                    parts.Add(prefix+text value)
                | _ -> invalid "Unsupported Ren'Py argument layout."
            if present (field "extrapos" node) then parts.Add("*"+str "extrapos" node)
            if present (field "extrakw" node) then parts.Add("**"+str "extrakw" node)
            "("+String.concat ", " parts+")"

    let parameters node =
        if not (present node) then ""
        else
            let parts=ResizeArray<string>()
            let parameter name value = name+(if present value then "="+text value else "")
            match field "parameters" node with
            | PyDict items ->
                let mutable kind=0
                for key,value in items do
                    let next=number "kind" 1 value
                    if kind=0 && next<>0 && parts.Count>0 then parts.Add("/")
                    if next=3 && kind<2 then parts.Add("*")
                    let prefix=if next=2 then "*" elif next=4 then "**" else ""
                    parts.Add(parameter (prefix+text key) (field "default" value))
                    kind<-next
                if kind=0 && parts.Count>0 then parts.Add("/")
            | raw ->
                let entries values = values |> Array.map (fun p -> match PickleData.values p with [|a;b|] -> text a,b | _ -> invalid "Unsupported Ren'Py parameter layout.")
                let all=entries (values raw)
                let only=entries (list "positional_only" node)
                let keyword=
                    if present (field "keyword_only" node) then entries (list "keyword_only" node)
                    elif present (field "positional" node) then
                        let positional=list "positional" node |> Array.map text |> Set.ofArray
                        all |> Array.filter(fun (n,_)->not (positional.Contains(n)))
                    else [||]
                let accounted=Array.append only keyword |> Array.map fst |> Set.ofArray
                for n,v in only do parts.Add(parameter n v)
                if only.Length>0 then parts.Add("/")
                for n,v in all do if not (accounted.Contains(n)) then parts.Add(parameter n v)
                if present (field "extrapos" node) then parts.Add("*"+str "extrapos" node)
                elif keyword.Length>0 then parts.Add("*")
                for n,v in keyword do parts.Add(parameter n v)
                if present (field "extrakw" node) then parts.Add("**"+str "extrakw" node)
            "("+String.concat ", " parts+")"

    let imageSpec node =
        let a=values node
        if a.Length<7 then invalid "Unsupported Ren'Py image specification."
        let result=ResizeArray<string>()
        result.Add(if present a[1] then "expression "+text a[1] else joined " " (values a[0]))
        if present a[2] then result.Add("as "+text a[2])
        if (values a[6]).Length>0 then result.Add("behind "+joined ", " (values a[6]))
        if present a[4] then result.Add("onlayer "+text a[4])
        if present a[5] then result.Add("zorder "+text a[5])
        if (values a[3]).Length>0 then result.Add("at "+joined ", " (values a[3]))
        String.concat " " result

    let render (nodes:PyValue[]) (cancellation:CancellationToken) =
        let output=StringBuilder("# Decompiled by GARbro; source expressions are not executed.\n")
        let active=HashSet<obj>(ReferenceEqualityComparer.Instance)
        let mutable prefix=""
        let line level (value:string) =
            cancellation.ThrowIfCancellationRequested()
            if level>128 || output.Length>ScriptBinary.maximum then invalid "Script structure exceeds the size limit."
            output.Append(' ',level*4).Append(prefix).Append(value).Append('\n') |> ignore
            prefix<-""
        let python level heading (source:string) =
            let source=source.Replace("\r\n","\n").TrimStart('\n')
            let lines=source.Split('\n')
            let content=lines |> Array.filter (fun l -> not (String.IsNullOrWhiteSpace(l)))
            let indent=if content.Length=0 then 0 else content |> Array.map (fun s -> s.Length-s.TrimStart(' ').Length) |> Array.min
            line level heading
            if content.Length=0 then line (level+1) "pass"
            else for s in lines do line (level+1) (if s.Length>=indent then s.Substring(indent) else "")
        let rec block level (items:PyValue[]) =
            let before=output.Length
            let mutable i=0
            while i<items.Length do
                let node=items[i]
                if name node="Call" && i+1<items.Length && name items[i+1]="Label" && (list "block" items[i+1]).Length=0 then
                    let call="call "+(if isTrue "expression" false node then "expression " else "")+str "label" node+
                             (if isTrue "expression" false node && present (field "arguments" node) then " pass " else "")+
                             arguments (field "arguments" node)+" from "+labelName items[i+1]
                    line level call
                    i<-i+1
                else emit level node
                i<-i+1
            if output.Length=before then line level "pass"
        and lex level nodes =
            for item in values nodes do
                let a=values item
                if a.Length<>4 && a.Length<>5 then invalid "Unsupported Ren'Py lexer block."
                line level (text a[a.Length-2])
                if (values a[a.Length-1]).Length>0 then lex (level+1) a[a.Length-1]
        and atl level node =
            if level>128 || not (active.Add(box node)) then invalid "Cyclic or excessively nested Ren'Py AST."
            try
                let nested heading child = line level heading; atl (level+1) child
                match name node with
                | "RawBlock" ->
                    if isTrue "animation" false node then line level "animation"
                    let statements=list "statements" node
                    if statements.Length=0 then line level "pass"
                    for child in statements do atl level child
                | "RawMultipurpose" ->
                    let words=ResizeArray<string>()
                    let duration=str "duration" node
                    if present (field "warp_function" node) then words.Add("warp "+str "warp_function" node+" "+duration)
                    elif present (field "warper" node) then words.Add(str "warper" node+" "+duration)
                    elif duration<>"" && duration<>"0" then words.Add("pause "+duration)
                    if present (field "revolution" node) then words.Add(str "revolution" node)
                    if str "circles" node<>"" && str "circles" node<>"0" then words.Add("circles "+str "circles" node)
                    for spline in list "splines" node do
                        let a=values spline
                        let expressions=values a[1]
                        if expressions.Length=0 then invalid "Invalid Ren'Py ATL spline."
                        words.Add(text a[0]+" "+text expressions[expressions.Length-1])
                        for e in expressions[..expressions.Length-2] do words.Add("knot "+text e)
                    for property in list "properties" node do
                        let a=values property
                        words.Add(text a[0]+" "+text a[1])
                    let expressions=list "expressions" node
                    for expression in expressions do
                        let a=values expression
                        words.Add(text a[0]+(if present a[1] then " with "+text a[1] else ""))
                        if expressions.Length>1 then words.Add("pass")
                    line level (if words.Count=0 then "pass" else String.concat " " words)
                | "RawChild" -> for child in list "children" node do nested "contains:" child
                | "RawChoice" ->
                    for choice in list "choices" node do
                        let a=values choice
                        nested ("choice "+text a[0]+":") a[1]
                    line level "pass"
                | "RawParallel" ->
                    for child in list "blocks" node do nested "parallel:" child
                    line level "pass"
                | "RawOn" -> for key,value in pairs (field "handlers" node) do nested ("on "+text key+":") value
                | "RawContainsExpr" -> line level ("contains "+str "expression" node)
                | "RawEvent" -> line level ("event "+str "name" node)
                | "RawFunction" -> line level ("function "+str "expr" node)
                | "RawRepeat" -> line level ("repeat"+optional "repeats" " " node)
                | "RawTime" -> line level ("time "+str "time" node)
                | other -> invalid ("Unsupported Ren'Py ATL node: "+other)
            finally active.Remove(box node) |> ignore
        and screenBody level node =
            let ordered=ResizeArray<int*Choice<string,PyValue>>()
            for item in list "keyword" node do
                let a=values item
                ordered.Add(number "linenumber" 0 a[1],Choice1Of2(text a[0]+(if present a[1] then " "+text a[1] else "")))
            for item in list "children" node do
                let location=values (field "location" item)
                let row=if location.Length>1 then match location[1] with PyInteger n -> int n | _ -> 0 else 0
                ordered.Add(row,Choice2Of2 item)
            if ordered.Count=0 && not (present (field "atl_transform" node)) then line level "pass"
            for _,item in ordered |> Seq.sortBy fst do
                match item with Choice1Of2 text -> line level text | Choice2Of2 child -> screen level child
            if present (field "atl_transform" node) then line level "at transform:"; atl (level+1) (field "atl_transform" node)
        and screen level node =
            if level>128 || not (active.Add(box node)) then invalid "Cyclic or excessively nested Ren'Py AST."
            try
                let nested heading = line level (heading+":"); screenBody (level+1) node
                match name node with
                | "SLScreen" -> nested ("screen "+str "name" node+parameters (field "parameters" node))
                | "SLBlock" -> screenBody level node
                | "SLIf" | "SLShowIf" as kind ->
                    for i,item in list "entries" node |> Array.indexed do
                        let a=values item
                        let heading=if not (present a[0]) then "else" else (if i=0 then (if kind="SLShowIf" then "showif " else "if ") else "elif ")+text a[0]
                        line level (heading+":");screenBody (level+1) a[1]
                | "SLFor" ->
                    let mutable children=list "children" node
                    let mutable variable=str "variable" node
                    if variable="_sl2_i" && children.Length>0 && name children[0]="SLPython" then
                        let source=code (field "code" children[0])
                        let separator=source.LastIndexOf("=",StringComparison.Ordinal)
                        if separator<0 then invalid "Unsupported Ren'Py screen tuple assignment."
                        variable<-source.Substring(0,separator).Trim()
                        children<-children[1..]
                    line level ("for "+variable+optional "index_expression" " index " node+" in "+str "expression" node+":")
                    if children.Length=0 then line (level+1) "pass"
                    for child in children do screen (level+1) child
                | "SLContinue" -> line level "continue"
                | "SLBreak" -> line level "break"
                | "SLPass" -> line level "pass"
                | "SLTransclude" -> line level "transclude"
                | "SLDefault" -> line level ("default "+str "variable" node+" = "+str "expression" node)
                | "SLPython" ->
                    let source=code (field "code" node)
                    if source.StartsWith("\n") || source.StartsWith(" ") then python level "python:" source else line level ("$ "+source)
                | "SLUse" ->
                    let target=field "target" node
                    let args=arguments (field "args" node)
                    let expression=match target with PyObject o when o.Name="PyExpr" -> true | _ -> false
                    let heading="use "+(if expression then "expression " else "")+text target+(if expression && args<>"" then " pass " else "")+args+optional "id" " id " node
                    if present (field "block" node) then line level (heading+":");screenBody (level+1) (field "block" node)
                    else line level heading
                | "SLDisplayable" ->
                    let displayable=field "displayable" node
                    let key=match displayable with PyGlobal(_,n) -> n | PyObject o -> o.Name | _ -> invalid "Unsupported Ren'Py screen displayable."
                    let style=str "style" node
                    let displayName=
                        match key,style with
                        | "MultiBox",("hbox"|"vbox"|"fixed") -> style
                        | "Window",("window"|"frame") -> style
                        | "_textbutton",_ -> "textbutton"
                        | "_label",_ -> "label"
                        | "_imagebutton",_ -> "imagebutton"
                        | "sl2add",_ | "_add",_ -> "add"
                        | "sl2bar",_ -> "bar" | "sl2vbar",_ -> "vbar"
                        | "sl2viewport",_ -> "viewport" | "sl2vpgrid",_ -> "vpgrid"
                        | "_hotbar",_ -> "hotbar" | "_hotspot",_ -> "hotspot" | "_imagemap",_ -> "imagemap" | "_key",_ -> "key"
                        | "Text",_ -> "text" | "image",_ -> "image" | "Transform",_ -> "transform"
                        | "AreaPicker",_ -> "areapicker" | "DismissBehavior",_ -> "dismiss"
                        | "OnEvent",_ -> "on" | "MouseArea",_ -> "mousearea" | "DragGroup",_ -> "draggroup"
                        | ("Button"|"Input"|"Timer"|"Drag"|"Grid"|"NearRect"|"Null"|"Side"),_ -> key.ToLowerInvariant()
                        | _ -> invalid ("Unsupported Ren'Py screen displayable: "+key)
                    let heading=displayName+(if (list "positional" node).Length=0 then "" else " "+joined " " (list "positional" node))
                    if (list "children" node).Length>0 || (list "keyword" node).Length>0 || present (field "atl_transform" node) then nested heading
                    else line level heading
                | other -> invalid ("Unsupported Ren'Py screen node: "+other)
            finally active.Remove(box node) |> ignore
        and testExpression depth node =
            if depth>128 then invalid "Script structure exceeds the size limit."
            let child key=testExpression (depth+1) (field key node)
            match name node with
            | "TextSelector" ->
                (if isTrue "raw" false node then "raw " else "")+
                (if isTrue "expression" false node then "expression "+str "pattern" node else quote (str "pattern" node))+
                (if isTrue "wait_for_focus" false node then " focused" else "")
            | "DisplayableSelector" ->
                (optional "screen" "screen " node+optional "id" " id " node+optional "layer" " layer " node).Trim()+
                (if isTrue "wait_for_focus" false node then " focused" else "")
            | "Not" -> "not ("+child "condition"+")"
            | "And" -> "("+child "left"+" and "+child "right"+")"
            | "Or" -> "("+child "left"+" or "+child "right"+")"
            | "Eval" -> "eval ("+str "expr" node+")"
            | "Label" -> "label "+str "name" node
            | "Click" | "Move" | "Scroll" as kind ->
                let selector=field "selector" node
                let selected=if present selector then " "+testExpression (depth+1) selector elif present (field "pattern" node) then " "+quote (str "pattern" node) else ""
                kind.ToLowerInvariant()+selected+optional "position" " pos " node+
                (if number "button" 1 node<>1 then " button "+str "button" node else "")+
                (if isTrue "always" false node then " always" else "")
            | "Advance" -> "advance"
            | "Pass" -> "pass"
            | "Exit" -> "exit"
            | "Skip" -> "skip"+(if isTrue "fast" false node then " fast" else "")
            | "Until" -> child "left"+" until "+child "right"+optional "timeout" " timeout " node
            | "Repeat" -> child "left"+" repeat "+str "count" node+optional "timeout" " timeout " node
            | "Keysym" -> "keysym "+quote (str "keysym" node)+(if present (field "selector" node) then " "+child "selector" else "")+optional "position" " pos " node
            | "Type" ->
                let typed=if present (field "text" node) then str "text" node else joined "" (list "keys" node)
                "type "+quote typed+(if present (field "selector" node) then " "+child "selector" else optional "pattern" " pattern " node)+optional "position" " pos " node
            | "Assert" -> "assert "+(if present (field "condition" node) then child "condition" else str "expr" node)+optional "timeout" " timeout " node
            | "Action" -> "run "+str "expr" node
            | "Pause" -> "pause "+str "expr" node
            | "Jump" -> "jump "+str "target" node
            | "Call" -> "call "+str "target" node
            | other -> invalid ("Unsupported Ren'Py testcase node: "+other)
        and testNodes level (nodes:PyValue[]) =
            if level>128 then invalid "Script structure exceeds the size limit."
            if nodes.Length=0 then line level "pass"
            for item in nodes do
                match name item with
                | "Python" ->
                    let source=if present (field "source" item) then str "source" item else code (field "code" item)
                    if source.Contains('\n') then python level "python:" source else line level ("$ "+source)
                | "If" ->
                    if present (field "entries" item) then
                        for i,entry in list "entries" item |> Array.indexed do
                            let a=values entry
                            line level ((if i=0 then "if " else "elif ")+testExpression 0 a[0]+":")
                            testNodes (level+1) (list "block" a[1])
                    else line level ("if "+str "condition" item+":");testNodes (level+1) (list "block" item)
                | _ -> line level (testExpression 0 item)
        and testcase level test =
            if level>128 then invalid "Script structure exceeds the size limit."
            if (list "parameters" test).Length<>0 || isTrue "only" false test || not (isTrue "enabled" true test) then invalid "Unsupported Ren'Py testcase parameters."
            let heading=if name test="TestSuite" then "testsuite " else "testcase "
            line level (heading+str "name" test+":")
            let mutable any=false
            if name test="TestSuite" then
                for hook in ["setup";"before_testsuite";"before_testcase";"after_testsuite";"after_testcase";"teardown"] do
                    let value=field hook test
                    if present value then
                        any<-true
                        line (level+1) (hook.Replace('_',' ')+":")
                        testNodes (level+2) (list "block" value)
                for child in list "subtests" test do any<-true;testcase (level+1) child
            let children=list "block" test
            if children.Length<>0 || not any then testNodes (level+1) children
        and emit level node =
            cancellation.ThrowIfCancellationRequested()
            if level>128 || not (active.Add(box node)) then invalid "Cyclic or excessively nested Ren'Py AST."
            try
                let kind=name node
                let child heading key = line level (heading+":");block (level+1) (list key node)
                let pythonSource () = code (field "code" node)
                match kind with
                | "Label" ->
                    let label=labelName node
                    child ("label "+label+parameters (field "parameters" node)+(if isTrue "hide" false node then " hide" else "")) "block"
                | "Say" | "TranslateSay" ->
                    let mutable at=level
                    if kind="TranslateSay" && present (field "language" node) then
                        line level ("translate "+str "language" node+" "+str "identifier" node+":");at<-level+1
                    let words=ResizeArray<string>()
                    if present (field "who" node) then words.Add(str "who" node)
                    for attr in list "attributes" node do words.Add(text attr)
                    if (list "temporary_attributes" node).Length>0 then words.Add("@");for attr in list "temporary_attributes" node do words.Add(text attr)
                    words.Add(sayString (str "what" node))
                    if not (isTrue "interact" true node) then words.Add("nointeract")
                    if present (field "identifier" node) then words.Add("id "+str "identifier" node)
                    if present (field "arguments" node) then words.Add(arguments (field "arguments" node))
                    if present (field "with_" node) then words.Add("with "+str "with_" node)
                    line at (String.concat " " words)
                | "Init" ->
                    let children=list "block" node
                    let priority=number "priority" 0 node
                    if children.Length=1 then
                        let expected=match name children[0] with "Screen" -> -500 | "Image" -> 500 | "Testcase" -> 500 | _ -> 0
                        if priority<>expected then prefix<-prefix+"init "+string priority+" "
                        emit level children[0]
                    else child ("init "+string priority) "block"
                | "Python" | "EarlyPython" ->
                    let source=pythonSource()
                    if kind="Python" && not (source.StartsWith("\n") || source.StartsWith(" ")) then line level ("$ "+source)
                    else
                        let store=str "store" node
                        python level ("python"+(if kind="EarlyPython" then " early" else "")+(if isTrue "hide" false node then " hide" else "")+(if store<>"" && store<>"store" then " in "+store.Replace("store.","") else "")+":") source
                | "Define" | "Default" ->
                    let store=str "store" node
                    let variable=(if store<>"" && store<>"store" then store.Replace("store.","")+"." else "")+str "varname" node
                    let index=if present (field "index" node) then "["+code (field "index" node)+"]" else ""
                    let operator=if kind="Default" || str "operator" node="" then "=" else str "operator" node
                    line level (kind.ToLowerInvariant()+" "+variable+index+" "+operator+" "+pythonSource())
                | "Jump" -> line level ("jump "+(if isTrue "expression" false node then "expression " else "")+str "target" node)
                | "Call" -> line level ("call "+(if isTrue "expression" false node then "expression " else "")+str "label" node+arguments (field "arguments" node))
                | "Return" -> line level ("return"+optional "expression" " " node)
                | "Pass" -> line level "pass"
                | "If" ->
                    let entries=list "entries" node
                    for i,entry in entries |> Array.indexed do
                        let a=values entry
                        if a.Length<>2 then invalid "Invalid Ren'Py conditional."
                        let isElse=i=entries.Length-1 && (match a[0] with PyText "True" | PyBool true -> true | _ -> false)
                        line level ((if isElse then "else" else (if i=0 then "if " else "elif ")+text a[0])+":")
                        block (level+1) (values a[1])
                | "While" -> child ("while "+str "condition" node) "block"
                | "Menu" ->
                    line level ("menu"+arguments (field "arguments" node)+":")
                    if present (field "set" node) then line (level+1) ("set "+str "set" node)
                    if present (field "with_" node) then line (level+1) ("with "+str "with_" node)
                    let itemArgs=list "item_arguments" node
                    let items=list "items" node
                    if itemArgs.Length<>0 && itemArgs.Length<>items.Length then invalid "Invalid Ren'Py menu item."
                    for i,item in items |> Array.indexed do
                        let a=values item
                        if a.Length<>3 then invalid "Invalid Ren'Py menu item."
                        let condition=if present a[1] && (match a[1] with PyText "True" | PyBool true -> false | _ -> true) then " if "+text a[1] else ""
                        let caption=sayString (text a[0])+(if itemArgs.Length=0 then "" else arguments itemArgs[i])
                        if present a[2] then line (level+1) (caption+condition+":");block (level+2) (values a[2])
                        else line (level+1) caption
                | "Image" ->
                    let heading="image "+joined " " (list "imgname" node)
                    if present (field "atl" node) then line level (heading+":");atl (level+1) (field "atl" node)
                    else line level (heading+" = "+pythonSource())
                | "Transform" ->
                    let store=str "store" node
                    line level ("transform "+(if store<>"" && store<>"store" then store.Replace("store.","")+"." else "")+str "varname" node+parameters (field "parameters" node)+":")
                    atl (level+1) (field "atl" node)
                | "Show" | "Scene" | "Hide" ->
                    let heading=kind.ToLowerInvariant()+(if present (field "imspec" node) then " "+imageSpec (field "imspec" node) else optional "layer" " onlayer " node)
                    if present (field "atl" node) then line level (heading+":");atl (level+1) (field "atl" node)
                    else line level heading
                | "ShowLayer" | "Camera" ->
                    let heading=(if kind="Camera" then "camera" else "show layer")+optional "layer" " " node+(if (list "at_list" node).Length>0 then " at "+joined ", " (list "at_list" node) else "")
                    if present (field "atl" node) then line level (heading+":");atl (level+1) (field "atl" node) else line level heading
                | "With" -> if not (present (field "paired" node)) then line level ("with "+str "expr" node)
                | "UserStatement" -> line level (str "line" node);if present (field "block" node) then lex (level+1) (field "block" node)
                | "Screen" -> screen level (field "screen" node)
                | "Testcase" ->
                    let test=field "test" node
                    if present (field "name" test) then testcase level test
                    else line level ("testcase "+str "label" node+":");testNodes (level+1) (list "block" test)
                | "Style" ->
                    let heading="style "+str "style_name" node+optional "parent" " is " node+(if isTrue "clear" false node then " clear" else "")+optional "take" " take " node+optional "variant" " variant " node
                    let properties=pairs (field "properties" node)
                    let deleted=list "delattr" node
                    if properties.Length=0 && deleted.Length=0 then line level heading
                    else
                        line level (heading+":")
                        for p in deleted do line (level+1) ("del "+text p)
                        for key,value in properties do line (level+1) (text key+" "+text value)
                | "Translate" -> child ("translate "+(if present (field "language" node) then str "language" node else "None")+" "+str "identifier" node) "block"
                | "TranslateString" ->
                    line level ("translate "+(if present (field "language" node) then str "language" node else "None")+" strings:")
                    line (level+1) ("old "+quote (str "old" node));line (level+1) ("new "+quote (str "new" node))
                | "TranslateBlock" | "TranslateEarlyBlock" ->
                    prefix<-prefix+"translate "+(if present (field "language" node) then str "language" node else "None")+" "
                    block level (list "block" node)
                | "EndTranslate" -> ()
                | "RPY" -> line level ("rpy "+joined " " (list "rest" node))
                | other -> invalid ("Unsupported Ren'Py AST node: "+other)
            finally active.Remove(box node) |> ignore
        if nodes.Length>0 then block 0 nodes
        output.ToString()

[<AbstractClass; Sealed>]
type RenpyScript private () =
    static member Export(data:byte[], cancellation:CancellationToken) =
        let payload=
            if data.AsSpan().StartsWith("RENPY RPC2"B) then
                use input=new MemoryStream(data,false)
                use reader=new BinaryReader(input)
                input.Position<-10L
                let mutable compressed=None
                let mutable doneHeader=false
                let ranges=ResizeArray<int64*int64>()
                while not doneHeader do
                    let slot=reader.ReadUInt32()
                    if slot=0u then doneHeader<-true
                    else
                        let offset=int64 (reader.ReadUInt32())
                        let length=int64 (reader.ReadUInt32())
                        if slot>1024u || offset<10L || offset>input.Length-length || length<=0L || ranges.Count>1024 then invalid "Invalid Ren'Py slot table."
                        if ranges |> Seq.exists (fun (a,b)->offset<b && a<offset+length) then invalid "Overlapping Ren'Py slots."
                        ranges.Add(offset,offset+length)
                        if slot=1u then
                            if compressed.IsSome then invalid "Duplicate Ren'Py AST slot."
                            compressed<-Some(data[int offset..int (offset+length-1L)])
                if ranges |> Seq.exists (fun (start,_)->start<input.Position) then invalid "Invalid Ren'Py slot offset."
                compressed |> Option.defaultWith(fun ()->invalid "Ren'Py AST slot is missing.")
            else data
        let decoded=ScriptBinary.inflate payload ScriptBinary.maximum cancellation
        let root=PickleData.load decoded cancellation |> values
        if root.Length<>2 then invalid "Unsupported Ren'Py script root."
        RenpySource.render (values root[1]) cancellation
