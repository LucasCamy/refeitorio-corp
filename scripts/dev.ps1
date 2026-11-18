
param(
    [Parameter(Mandatory)]
    [ValidateSet('setup','api','web','test')]
    [string]$Action
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-Location -LiteralPath (Split-Path $PSScriptRoot -Parent)

function New-LocalSecret {
    $bytes = [byte[]]::new(24)
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return ([BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant())
}

function Invoke-Step {
    param([string]$Program, [string[]]$Arguments)
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program encerrou com código $LASTEXITCODE." }
}

if ($Action -eq 'web') {
    Invoke-Step npm @('--prefix','apps/web','run','dev')
    return
}

$environmentFile = Join-Path (Get-Location) '.env'
if ($Action -eq 'setup' -and -not (Test-Path -LiteralPath $environmentFile)) {
    [IO.File]::WriteAllLines($environmentFile, @(
        "DB_PASSWORD=$(New-LocalSecret)",
        "SANDBOX_PASSWORD=$(New-LocalSecret)"
    ))
}
if (-not (Test-Path -LiteralPath $environmentFile)) { throw 'Execute scripts/dev.ps1 setup primeiro.' }

$config = @{}
foreach ($line in (Get-Content -LiteralPath $environmentFile)) {
    if ($line -match '^(DB_PASSWORD|SANDBOX_PASSWORD)=([0-9a-f]{32,64})$') {
        $config[$Matches[1]] = $Matches[2]
    }
}
if (-not $config.DB_PASSWORD -or -not $config.SANDBOX_PASSWORD) { throw 'Confira a configuração local em .env.' }

$env:DB_PASSWORD = $config.DB_PASSWORD
$env:DB_PORT = if ($env:DB_PORT) { $env:DB_PORT } else { '55432' }
$env:ConnectionStrings__Refeitorio = "Host=127.0.0.1;Port=$($env:DB_PORT);Database=refeitorio;Username=refeitorio;Password=$($config.DB_PASSWORD);Timeout=4;Command Timeout=15"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Hr__Mode = 'Sandbox'
$env:Sandbox__Password = $config.SANDBOX_PASSWORD
$env:ASPNETCORE_URLS = 'http://localhost:5088'

switch ($Action) {
    'setup' {
        Invoke-Step docker @('compose','-p','refeitorio-sandbox','up','-d','--wait')
        Invoke-Step dotnet @('run','--project','apps/api/Restaurante.Api.csproj','--no-launch-profile','--','--migrate','--seed')
        Invoke-Step npm @('--prefix','apps/web','ci')
        Write-Host 'Usuários fictícios: operador, caixa2, gerente, admin. Senha local: variável SANDBOX_PASSWORD em .env.'
    }
    'api' { Invoke-Step dotnet @('run','--project','apps/api/Restaurante.Api.csproj','--no-launch-profile') }
    'test' { Invoke-Step dotnet @('test','tests/Restaurante.Tests/Restaurante.Tests.csproj','-c','Release') }
}
