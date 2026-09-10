$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpfDir = Join-Path $frameworkDir 'WPF'
$output = Join-Path $projectDir 'NetPulse.exe'
$presentationCore = '/reference:' + (Join-Path $wpfDir 'PresentationCore.dll')
$presentationFramework = '/reference:' + (Join-Path $wpfDir 'PresentationFramework.dll')
$windowsBase = '/reference:' + (Join-Path $wpfDir 'WindowsBase.dll')
$systemXaml = '/reference:' + (Join-Path $frameworkDir 'System.Xaml.dll')
$systemNetHttp = '/reference:' + (Join-Path $frameworkDir 'System.Net.Http.dll')

if (-not (Test-Path -LiteralPath $compiler)) {
    throw '未找到系统自带的 .NET Framework C# 编译器。'
}

& $compiler /nologo /target:winexe /optimize+ /platform:anycpu `
    /out:$output `
    $presentationCore `
    $presentationFramework `
    $windowsBase `
    $systemXaml `
    $systemNetHttp `
    (Join-Path $projectDir 'Program.cs')

if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码：$LASTEXITCODE"
}

Write-Host "构建完成：$output"
