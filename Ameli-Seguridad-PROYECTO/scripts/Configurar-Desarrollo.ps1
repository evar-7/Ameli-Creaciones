param(
    [string]$ConnectionString = 'Server=(localdb)\MSSQLLocalDB;Database=AmeliSecurity;Trusted_Connection=True;TrustServerCertificate=True',
    [switch]$CrearUsuariosDemo
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$api = Join-Path $root 'src/Ameli.Api/Ameli.Api.csproj'
$previousEncoding = $OutputEncoding
function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw 'El comando dotnet no se completó. Revisa el mensaje anterior.' }
}
function Read-PrivateText([string]$prompt) {
    $secure = Read-Host $prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}
function Test-Password([string]$value) {
    return $value.Length -ge 8 -and $value.Length -le 64 -and $value -cmatch '\p{Lu}' -and $value -cmatch '\p{Ll}' -and $value -match '\d' -and $value -match '[^\p{L}\p{N}\s]'
}
Push-Location $root
try {
    $OutputEncoding = New-Object System.Text.UTF8Encoding($false)
    Write-Host 'Preparación de Ameli: API .NET 10 + Blazor + SQL Server.'
    Invoke-DotNet --version
    Invoke-DotNet restore Ameli.Seguridad.sln
    Invoke-DotNet dev-certs https --trust
    $email = Read-Host 'Correo del administrador inicial'
    if ($email -notmatch '^[^\s@]+@[^\s@]+\.[^\s@]+$') { throw 'Escribe un correo válido.' }
    $adminPassword = Read-PrivateText 'Contraseña: 8-64 caracteres, mayúscula, minúscula, número y símbolo'
    if (-not (Test-Password $adminPassword)) { throw 'La contraseña no cumple la política.' }
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = New-Object byte[] 48
    $random.GetBytes($bytes); $random.Dispose()
    $values = @{
        'ConnectionStrings:DefaultConnection' = $ConnectionString
        'Jwt:SigningKey' = [Convert]::ToBase64String($bytes)
        'Bootstrap:AdminEmail' = $email
        'Bootstrap:AdminPassword' = $adminPassword
        'Bootstrap:CreateDemoUsers' = [bool]$CrearUsuariosDemo
        'Email:Mode' = 'Pickup'
    }
    if ($CrearUsuariosDemo) {
        $demoPassword = Read-PrivateText 'Contraseña común para las tres cuentas de prueba'
        if (-not (Test-Password $demoPassword)) { throw 'La contraseña de prueba no cumple la política.' }
        $values['Bootstrap:DemoPassword'] = $demoPassword
    }
    $values | ConvertTo-Json | & dotnet user-secrets set --project $api
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron guardar los secretos de desarrollo.' }
    Invoke-DotNet run --project $api --launch-profile https -- --initialize
    Invoke-DotNet user-secrets remove Bootstrap:AdminPassword --project $api
    if ($CrearUsuariosDemo) { Invoke-DotNet user-secrets remove Bootstrap:DemoPassword --project $api }
    Write-Host 'Base de datos y administrador preparados. Abre Ameli.Seguridad.sln y arranca Ameli.Api + Ameli.Web.'
    if ($CrearUsuariosDemo) { Write-Host 'Cuentas de prueba: cliente@ameli.test, logistica@ameli.test, admin2@ameli.test.' }
}
finally {
    $adminPassword = $null; $demoPassword = $null; $values = $null
    $OutputEncoding = $previousEncoding
    Pop-Location
}
