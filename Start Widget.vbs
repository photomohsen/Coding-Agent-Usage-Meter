' Starts the Coding Agent Usage Meter with no console window.
' It waits while the widget runs, so it can say why when Windows blocks the widget or the widget can't start.
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
title = "Coding Agent Usage Meter"
root = fso.GetParentFolderName(WScript.ScriptFullName)
dll = root & "\prototype\UsageWidget.Prototype\bin\Debug\net10.0-windows\UsageWidget.Prototype.dll"

If Not fso.FileExists(dll) Then
    MsgBox "The widget is not built yet." & vbCrLf & vbCrLf & _
        "Double-click Install.cmd in this folder to build it:" & vbCrLf & root, vbExclamation, title
    WScript.Quit 1
End If

' dotnet may be machine-wide or a user install in %USERPROFILE%\.dotnet that is not on PATH.
dotnet = "dotnet"
machineDotnet = shell.ExpandEnvironmentStrings("%ProgramFiles%") & "\dotnet\dotnet.exe"
userDotnet = shell.ExpandEnvironmentStrings("%USERPROFILE%") & "\.dotnet\dotnet.exe"
If fso.FileExists(machineDotnet) Then
    dotnet = machineDotnet
ElseIf fso.FileExists(userDotnet) Then
    dotnet = userDotnet
End If

Set started = CreateObject("WbemScripting.SWbemDateTime")
started.SetVarDate Now, True
startedAt = Timer
code = shell.Run("""" & dotnet & """ """ & dll & """", 0, True)
' 0: quit, or a second widget that found one already running. Anything else: it crashed.
If code = 0 Then WScript.Quit 0

If Blocked() Then
    MsgBox "Windows blocked the widget, so it did not start." & vbCrLf & vbCrLf & _
        "Smart App Control stops apps that are not signed. This widget is built on your PC, so it has no signature." & vbCrLf & vbCrLf & _
        "Start the widget again: Windows lets it run some of the time." & vbCrLf & _
        "To stop the blocks, turn off Smart App Control in Windows Security > App & browser control > Smart App Control settings.", _
        vbExclamation, title
ElseIf Abs(Timer - startedAt) < 30 Then
    MsgBox "The widget could not start (error " & Hex(code) & ")." & vbCrLf & vbCrLf & _
        "Double-click Install.cmd in this folder to build it again:" & vbCrLf & root, vbExclamation, title
End If
WScript.Quit code

' True when .NET logged, since we started it, that an Application Control policy (Smart App Control) blocked our dll.
' Windows writes the event a moment after the crash, so look a few times.
Function Blocked()
    Blocked = False
    Set wmi = GetObject("winmgmts:\\.\root\cimv2")
    For i = 1 To 5
        Set events = wmi.ExecQuery("SELECT Message FROM Win32_NTLogEvent WHERE Logfile = 'Application' " & _
            "AND SourceName = '.NET Runtime' AND EventCode = 1026 AND TimeWritten >= '" & started.Value & "'")
        For Each e In events
            If InStr(e.Message, "UsageWidget.Prototype.dll") > 0 And InStr(e.Message, "Application Control") > 0 Then
                Blocked = True
                Exit Function
            End If
        Next
        WScript.Sleep 1000
    Next
End Function
