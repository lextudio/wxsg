# If not running on Windows, skip Windows-only signing steps.
if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
    Write-Host "Not running on Windows; skipping Windows-only signing steps."
    exit 0
}

if ($env:CI -eq "true") {
    Write-Host "Running in CI environment; skipping signing steps when no cert."
    exit 0
}

$cert = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | Select-Object -First 1
if ($null -eq $cert) {
    Write-Host "No code signing certificate found in MY store. Exit."
    exit 1
}

Write-Host "Certificate found. Sign NuGet packages."

# Extract certificate subject name (remove CN=, etc.)
$subjectName = $cert.Subject -replace 'CN=([^,]+).*', '$1'

Write-Host "Sign NuGet packages."
& dotnet nuget sign artifacts\nuget\*.nupkg --certificate-subject-name "$subjectName" --timestamper http://timestamp.digicert.com
& dotnet nuget verify --all artifacts\nuget\*.nupkg
if ($LASTEXITCODE -ne 0)
{
    Write-Host "NuGet package is not signed. Exit."
    exit $LASTEXITCODE
}

Write-Host "Verification finished."
