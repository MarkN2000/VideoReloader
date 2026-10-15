$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$project = Join-Path $PSScriptRoot 'VideoReloader\VideoReloader.csproj'
dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$dll = Join-Path $PSScriptRoot 'VideoReloader\bin\Release\net10.0\VideoReloader.dll'
$outputDir = Join-Path $PSScriptRoot 'dist'
[IO.Directory]::CreateDirectory($outputDir) | Out-Null
$output = Join-Path $outputDir "VideoReloader-$version.zip"
$temporary = "$output.tmp"
$dependencies = @(
    'ResoniteModding-BepisLoader-1.7.0'
    'ResoniteModding-BepInExResoniteShim-0.9.3'
    'ResoniteModding-BepisResoniteWrapper-1.0.2'
    'ResoniteModding-BepisModSettings-1.6.6'
)
$manifest = [ordered]@{
    name = 'VideoReloader'
    author = 'markn2000'
    version_number = $version
    description = 'Retry video loading in your current session without duplicating players. Reload locally from Bepis Mod Settings or automatically after changing the cookie browser or Preferred Resolution.'
    website_url = 'https://github.com/MarkN2000/VideoReloader'
    dependencies = $dependencies
} | ConvertTo-Json
$documents = @('README.md', 'LICENSE', 'icon.png')

# Replace the completed ZIP only after validation passes.
try {
    $stream = [IO.File]::Open($temporary, [IO.FileMode]::Create)
    $zip = $null
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $dll, 'plugins/VideoReloader.dll') | Out-Null
        foreach ($file in $documents) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $PSScriptRoot $file), $file) | Out-Null
        }
        $writer = [IO.StreamWriter]::new($zip.CreateEntry('manifest.json').Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write($manifest) } finally { $writer.Dispose() }
    } finally { if ($zip) { $zip.Dispose() }; $stream.Dispose() }

    $zip = [IO.Compression.ZipFile]::OpenRead($temporary)
    try {
        $expected = $documents + @('manifest.json', 'plugins/VideoReloader.dll')
        if (Compare-Object $expected @($zip.Entries.FullName)) { throw 'Unexpected ZIP contents.' }
        $reader = [IO.StreamReader]::new($zip.GetEntry('manifest.json').Open())
        try { $metadata = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ($metadata.name -cne 'VideoReloader' -or $metadata.author -cne 'markn2000' -or
            $metadata.version_number -ne $version -or $metadata.description.Length -gt 250 -or
            $metadata.website_url -cne 'https://github.com/MarkN2000/VideoReloader' -or
            (@($metadata.dependencies) -join ',') -cne ($dependencies -join ',')) {
            throw 'Manifest metadata does not match.'
        }
        $entryStream = $zip.GetEntry('plugins/VideoReloader.dll').Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($entryStream)).Replace('-', '') }
        finally { $entryStream.Dispose(); $sha.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash) { throw 'Packaged DLL does not match the build output.' }
    } finally { $zip.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $output -Force
    Write-Output "Created and validated ZIP: $output"
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
