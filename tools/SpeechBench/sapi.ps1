param(
    [ValidateSet('synthesize', 'recognize', 'probe')][string]$Mode = 'probe',
    [string]$InputJson,
    [Parameter(Mandatory = $true)][string]$OutputJson,
    [int]$Repeats = 2
)
$ErrorActionPreference = 'Stop'
(Get-Process -Id $PID).ProcessorAffinity = [IntPtr]15
Add-Type -AssemblyName System.Speech
Add-Type -AssemblyName System.Web.Extensions
Add-Type -Path (Join-Path $PSScriptRoot 'SapiBridge.cs') -ReferencedAssemblies System.Speech, System.Web.Extensions
[SpeechBench.SapiBridge]::Run($Mode, $InputJson, $OutputJson, $Repeats)
