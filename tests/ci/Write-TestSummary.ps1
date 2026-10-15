<#
  Collects CI results into one human-readable TEST-RESULTS.md.
#>
param([Parameter(Mandatory = $true)][string]$ResultDir, [Parameter(Mandatory = $true)][string]$OutFile, [string]$Version = '')
$ErrorActionPreference = 'Stop'
$lines = New-Object System.Collections.Generic.List[string]
$os = (Get-CimInstance Win32_OperatingSystem)
$lines.Add("# EK Triangle $Version - automated test results")
$lines.Add('')
$lines.Add("Machine: $($os.Caption) $($os.Version) (GitHub Actions Windows runner)  ")
$lines.Add("Date (UTC): $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm'))  ")
$lines.Add("Commit: $env:GITHUB_SHA")
$lines.Add('')
$lines.Add('These are automated build, simulation and Windows checks. They do NOT replace the short manual check inside Revit (see docs/ACCEPTANCE-TEST.md); Revit itself is not available on CI.')
$lines.Add('')
$lines.Add('## Unit tests (simulated Revit installations)')
foreach ($trx in Get-ChildItem $ResultDir -Recurse -Filter '*.trx') {
    [xml]$x = Get-Content $trx.FullName
    $c = $x.TestRun.ResultSummary.Counters
    $storage = ($x.TestRun.TestDefinitions.UnitTest | Select-Object -First 1).storage
    $fw = if ($storage -match 'net48') { '.NET Framework 4.8' } elseif ($storage -match 'net8') { '.NET 8' } else { '' }
    $lines.Add("- $fw : total $($c.total), passed $($c.passed), failed $($c.failed)")
}
foreach ($f in 'native-tests.txt', 'payload-checks.txt', 'e2e\installer-e2e.txt', 'simple\simple-e2e.txt') {
    $p = Join-Path $ResultDir $f
    if (Test-Path $p) {
        $lines.Add('')
        $lines.Add("## $f")
        $lines.Add('```')
        Get-Content $p | ForEach-Object { $lines.Add($_) }
        $lines.Add('```')
    }
}
$lines | Set-Content $OutFile -Encoding UTF8
