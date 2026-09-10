$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\WorkMate\WorkMate.csproj"
$publishPath = Join-Path $repositoryRoot "artifacts\win-x64"
$iconPreparationScript = Join-Path $PSScriptRoot "PrepareIcons.py"

py -3 $iconPreparationScript
if ($LASTEXITCODE -ne 0) {
    throw "WorkMate icon preparation failed with exit code $LASTEXITCODE."
}

dotnet publish $projectPath `
    -c Release `
    -p:PublishProfile=win-x64 `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "WorkMate publish failed with exit code $LASTEXITCODE."
}

$publishedFiles = @(Get-ChildItem -LiteralPath $publishPath -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne "WorkMate.exe") {
    $names = $publishedFiles.Name -join ", "
    throw "Expected only WorkMate.exe in the publish folder, but found: $names"
}

Write-Host "Published: $($publishedFiles[0].FullName)"
Write-Host "Size: $([math]::Round($publishedFiles[0].Length / 1MB, 2)) MB"
