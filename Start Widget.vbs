' Starts the Claude Usage Widget with no console window.
Set fso = CreateObject("Scripting.FileSystemObject")
dll = fso.GetParentFolderName(WScript.ScriptFullName) & "\prototype\UsageWidget.Prototype\bin\Debug\net10.0-windows\UsageWidget.Prototype.dll"
CreateObject("WScript.Shell").Run "dotnet """ & dll & """", 0, False
