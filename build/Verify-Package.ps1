param([string]$PackageDirectory = 'artifacts/packages')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter 'FluentTaskScheduler.*.nupkg')
if ($packages.Count -ne 1) { throw 'Expected exactly one FluentTaskScheduler package.' }
$archive = [System.IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    foreach ($entry in @('lib/net10.0/FluentTaskScheduler.dll', 'README.md', 'FluentTaskScheduler.nuspec')) {
        if ($null -eq $archive.GetEntry($entry)) { throw "Package is missing $entry" }
    }
    if (@($archive.Entries | Where-Object { $_.FullName -match 'Tests|Sandbox|AotSmoke' }).Count -gt 0) {
        throw 'Package includes test or sample binaries.'
    }
    Write-Output 'Package contents verified.'
}
finally { $archive.Dispose() }
