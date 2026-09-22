$exe = 'C:\Users\sunghyuk\Documents\Leneu\win-cal\WinCal\bin\Release\net8.0-windows\win-x64\publish\WinCal.exe'
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\WinCal.lnk')
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = Split-Path $exe
$lnk.IconLocation = "$exe,0"
$lnk.Description = 'WinCal - Desktop calendar widget'
$lnk.Save()
Write-Output 'shortcut created'
