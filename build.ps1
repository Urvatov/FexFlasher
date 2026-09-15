# build.ps1
param (
    [string]$Runtime = "win-x64"
)

Write-Host "🚀 Building FexFlasher for $Runtime ..."

# Clean old builds
if (Test-Path "dist") { Remove-Item "dist" -Recurse -Force }

# Run publish
dotnet publish FexFlasher.csproj `
    -c Release `
    -r $Runtime `
    -p:PublishSingleFile=true `
    -p:SelfContained=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o "dist"

# Copy files folder with default fex if present
if (Test-Path "files") {
    Copy-Item "files" -Destination "dist\files" -Recurse -Force
}

Write-Host "`n✅ Build complete!"
Write-Host "Output folder: dist\"
Write-Host "Executable:    dist\FexFlasher.exe"
