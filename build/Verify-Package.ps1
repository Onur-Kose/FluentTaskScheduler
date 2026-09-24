param([string]$PackageDirectory = 'artifacts/packages')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg')
if ($packages.Count -ne 2) { throw "Expected scheduler and UI packages; found $($packages.Count)." }

$versions = @{}
foreach ($id in @('FluentTaskScheduler', 'FluentTaskScheduler.UI')) {
    $matches = @($packages | Where-Object { $_.Name -match "^$([regex]::Escape($id))\.[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?\.nupkg$" })
    if ($matches.Count -ne 1) { throw "Expected exactly one $id package." }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($matches[0].FullName)
    try {
        foreach ($entry in @("lib/net10.0/$id.dll", 'README.md', 'icon.png', "$id.nuspec")) {
            if ($null -eq $archive.GetEntry($entry)) { throw "$id package is missing $entry" }
        }
        if (@($archive.Entries | Where-Object { $_.FullName -match 'Tests|Sandbox|AotSmoke|UI.Demo' }).Count -gt 0) {
            throw "$id package includes test or sample files."
        }

        $reader = [System.IO.StreamReader]::new($archive.GetEntry("$id.nuspec").Open())
        try { [xml]$manifest = $reader.ReadToEnd() }
        finally { $reader.Dispose() }

        $version = $manifest.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='version']").InnerText
        $versions[$id] = $version
        if ($id -eq 'FluentTaskScheduler.UI') {
            $dependency = $manifest.SelectSingleNode("//*[local-name()='dependency' and @id='FluentTaskScheduler']")
            if ($null -eq $dependency) { throw 'UI package is missing its FluentTaskScheduler dependency.' }
            $versions['UI dependency'] = $dependency.GetAttribute('version')
            $framework = $manifest.SelectSingleNode("//*[local-name()='frameworkReference' and @name='Microsoft.AspNetCore.App']")
            if ($null -eq $framework) { throw 'UI package is missing the ASP.NET Core framework reference.' }
        }
    }
    finally { $archive.Dispose() }
}

if ($versions['FluentTaskScheduler'] -ne $versions['UI dependency']) {
    throw 'The UI dependency version must match the scheduler package version.'
}

Write-Output "Verified FluentTaskScheduler $($versions['FluentTaskScheduler']) and FluentTaskScheduler.UI $($versions['FluentTaskScheduler.UI']) packages."
