$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
dotnet build (Join-Path $root 'src/TheTinkererZhHans.csproj') -c Release -v quiet --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output "DLL: $root/src/bin/Release/net9.0/TheTinkererZhHans.dll"
Write-Output '工坊内容采用验证过的版本；构建不会自动覆盖已发布的 DLL 或上传。'
