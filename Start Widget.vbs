' Starts the Coding Agent Usage Meter with no console window.
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
dll = fso.GetParentFolderName(WScript.ScriptFullName) & "\prototype\UsageWidget.Prototype\bin\Debug\net10.0-windows\UsageWidget.Prototype.dll"

' dotnet may be machine-wide or a user install in %USERPROFILE%\.dotnet that is not on PATH.
dotnet = "dotnet"
machineDotnet = shell.ExpandEnvironmentStrings("%ProgramFiles%") & "\dotnet\dotnet.exe"
userDotnet = shell.ExpandEnvironmentStrings("%USERPROFILE%") & "\.dotnet\dotnet.exe"
If fso.FileExists(machineDotnet) Then
    dotnet = machineDotnet
ElseIf fso.FileExists(userDotnet) Then
    dotnet = userDotnet
End If

shell.Run """" & dotnet & """ """ & dll & """", 0, False
