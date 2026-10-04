Set WshShell = CreateObject(WScript.Shell)
WshShell.Run schtasks /run /tn Nitrous , 0
