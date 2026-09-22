$b = [IO.File]::ReadAllBytes("$PSScriptRoot\..\WinCal\Assets\AppIcon.ico")
$n = [BitConverter]::ToUInt16($b, 4)
$last = $n - 1
$off = [BitConverter]::ToUInt32($b, (6 + 16 * $last + 12))
$len = [BitConverter]::ToUInt32($b, (6 + 16 * $last + 8))
[IO.File]::WriteAllBytes("$PSScriptRoot\preview256.png", $b[$off..($off + $len - 1)])
Write-Output "entries=$n offset=$off len=$len"
