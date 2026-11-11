#r "bin/Release/net10.0/RuntimeCompactor.dll"

open Garbro.Build.RuntimeCompactor

let tracing = "System.Diagnostics.Tracing.EventLevel, System.Diagnostics.Tracing, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
let core = "System.Diagnostics.Tracing.EventLevel, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e"
assert (serializedTypeName tracing = serializedTypeName core)
assert (serializedTypeName ("System.Collections.Generic.List`1[[" + tracing + "]], System.Private.CoreLib") = "System.Collections.Generic.List`1<System.Diagnostics.Tracing.EventLevel>")
assert (serializedTypeName "System.Int32[,][]&, System.Private.CoreLib" = "System.Int32[,*][]&")
assert (attributeValue (box 1) <> attributeValue (box 2))

let original = { File="Example.dll"; Identity="Example/1.0.0.0/key"; Bytes=100L; ReadyToRun=true; Contract="members"; Resources="bytes"; ContractRows=[||] }
let rewritten = { original with Bytes=50L; ReadyToRun=false }
assert (preservesInventory original rewritten)
for changed in [ { rewritten with ReadyToRun=true }; { rewritten with Identity="other" }
                 { rewritten with Contract="missing member" }; { rewritten with Resources="changed bytes" } ] do
    assert (not (preservesInventory original changed))
assert (startupLibraries.Contains "System.Private.CoreLib.dll")
assert (compatibilityLibraries.Contains "System.Private.DataContractSerialization.dll")
printfn "Runtime compactor checks passed."
