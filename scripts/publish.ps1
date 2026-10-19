$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
Push-Location frontend
try {
  npm ci --no-audit --no-fund
  if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
  npm run build
  if ($LASTEXITCODE -ne 0) { throw 'frontend build failed' }
} finally { Pop-Location }
foreach ($project in @('Local', 'Platform', 'Components')) {
  dotnet publish "src/KillerBee.$project" --configuration Release --runtime win-x64 --self-contained false --output "artifacts/$project"
  if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}
Write-Host 'Packages ready in artifacts/Local, artifacts/Platform and artifacts/Components.'
Write-Host 'Copy Components to each of the two VM. Keep secrets and certificates outside the repository.'
