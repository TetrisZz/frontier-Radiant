param([switch]$Prepare, [string]$Manifest, [int]$Start = 0, [int]$Count = 10)
$ErrorActionPreference = 'Stop'
$baseRef = 'a9f202731b2a16c3e0025ec5266499f6cf1600a5'
$headRef = 'refs/remotes/pr-import/159'
$utf8 = [System.Text.UTF8Encoding]::new($false)
if ($Prepare) {
    $scratch = Join-Path (Get-Location) ('Tools/_radiant/pr159-scratch-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch | Out-Null
    $items = @()
    $conflicts = @()
    $binary = @()
    foreach ($line in (git diff --no-renames --numstat $baseRef $headRef)) {
        $parts = $line -split "`t"
        $path = $parts[2]
        if ($path -eq 'RobustToolbox') { continue }
        if ($parts[0] -eq '-') { $binary += $path; continue }
        $slot = Join-Path $scratch ([string]$items.Count)
        New-Item -ItemType Directory -Path $slot | Out-Null
        $baseLines = @(git show "${baseRef}:$path" 2>$null)
        $baseExists = $LASTEXITCODE -eq 0
        $newLines = @(git show "${headRef}:$path" 2>$null)
        $newExists = $LASTEXITCODE -eq 0
        $oldExists = Test-Path -LiteralPath $path
        $oldText = if ($oldExists) { [System.IO.File]::ReadAllText((Join-Path (Get-Location) $path)).Replace("`r`n", "`n") } else { '' }
        $baseText = if ($baseExists) { ($baseLines -join "`n") + "`n" } else { '' }
        $newText = if ($newExists) { ($newLines -join "`n") + "`n" } else { '' }
        [System.IO.File]::WriteAllText((Join-Path $slot 'before'), $oldText, $utf8)
        [System.IO.File]::WriteAllText((Join-Path $slot 'base'), $baseText, $utf8)
        [System.IO.File]::WriteAllText((Join-Path $slot 'upstream'), $newText, $utf8)
        $result = $newText
        $conflict = $false
        if ($baseExists -and $newExists -and $oldExists) {
            $merged = @(git merge-file -p (Join-Path $slot 'before') (Join-Path $slot 'base') (Join-Path $slot 'upstream'))
            $conflict = $LASTEXITCODE -ne 0
            $result = ($merged -join "`n") + "`n"
        } elseif (!$newExists -and $oldExists -and $oldText.TrimEnd() -ne $baseText.TrimEnd()) {
            $conflict = $true
        } elseif (!$baseExists -and $newExists -and $oldExists -and $oldText.TrimEnd() -ne $newText.TrimEnd()) {
            $conflict = $true
        }
        [System.IO.File]::WriteAllText((Join-Path $slot 'after'), $result, $utf8)
        $item = @{ Path=$path; Slot=$slot; OldExists=$oldExists; NewExists=$newExists; Conflict=$conflict }
        $items += $item
        if ($conflict) { $conflicts += $path }
    }
    $manifestPath = Join-Path $scratch 'manifest.json'
    [System.IO.File]::WriteAllText($manifestPath, (ConvertTo-Json -InputObject $items -Depth 4), $utf8)
    @{ Manifest=$manifestPath; Total=$items.Count; Conflicts=$conflicts; Binary=$binary } | ConvertTo-Json -Depth 4 -Compress
    exit
}
$items = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
$patches = @()
foreach ($item in ($items | Select-Object -Skip $Start -First $Count)) {
    if ($item.Conflict) { continue }
    $patch = "*** Begin Patch`n"
    if (!$item.NewExists) {
        if (!$item.OldExists) { continue }
        $patch += "*** Delete File: $($item.Path)`n"
    } elseif (!$item.OldExists) {
        $patch += "*** Add File: $($item.Path)`n"
        foreach ($line in [System.IO.File]::ReadAllLines((Join-Path $item.Slot 'after'))) { $patch += "+$line`n" }
    } else {
        $diff = @(git -c core.autocrlf=false diff --no-index -- (Join-Path $item.Slot 'before') (Join-Path $item.Slot 'after') 2>$null)
        if ($LASTEXITCODE -eq 0) { continue }
        $patch += "*** Update File: $($item.Path)`n"
        $hunks = $false
        $oldLine = 0
        $bytes = [System.IO.File]::ReadAllBytes((Join-Path (Get-Location) $item.Path))
        $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191
        foreach ($line in $diff) {
            if ($line.StartsWith('@@')) { $hunks=$true; $oldLine=[int]([regex]::Match($line, '^@@ -(\d+)').Groups[1].Value); $patch += "@@`n"; continue }
            if ($hunks -and !$line.StartsWith('\ No newline')) {
                if ($line.StartsWith(' ') -or $line.StartsWith('-')) {
                    if ($oldLine -eq 1 -and $hasBom) { $line = $line.Substring(0,1) + [char]0xFEFF + $line.Substring(1).TrimStart([char]0xFEFF) }
                    $oldLine++
                }
                $patch += "$line`n"
            }
        }
    }
    $patch += '*** End Patch'
    $patches += @{ Path=$item.Path; Patch=$patch }
}
ConvertTo-Json -InputObject $patches -Depth 4 -Compress
