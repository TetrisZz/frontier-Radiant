param(
    [Parameter(Mandatory)] [string] $Source,
    [Parameter(Mandatory)] [string] $Destination,
    [Parameter(Mandatory)] [string] $Copyright,
    [string[]] $States
)

Add-Type -AssemblyName System.Drawing

$bytes = [IO.File]::ReadAllBytes($Source)
$position = 8
$description = $null
while ($position + 12 -le $bytes.Length) {
    $length = ([int]$bytes[$position] -shl 24) -bor ([int]$bytes[$position + 1] -shl 16) -bor
        ([int]$bytes[$position + 2] -shl 8) -bor [int]$bytes[$position + 3]
    if ($length -lt 0 -or $position + 12 + $length -gt $bytes.Length) { break }
    if ([Text.Encoding]::ASCII.GetString($bytes, $position + 4, 4) -eq 'zTXt') {
        $chunk = [byte[]]$bytes[($position + 8)..($position + 7 + $length)]
        $separator = [Array]::IndexOf($chunk, [byte]0)
        if ([Text.Encoding]::ASCII.GetString($chunk, 0, $separator) -eq 'Description') {
            $compressed = [IO.MemoryStream]::new($chunk, $separator + 2, $chunk.Length - $separator - 2)
            $stream = [IO.Compression.ZLibStream]::new($compressed, [IO.Compression.CompressionMode]::Decompress)
            $uncompressed = [IO.MemoryStream]::new()
            $stream.CopyTo($uncompressed)
            $description = [Text.Encoding]::UTF8.GetString($uncompressed.ToArray())
            $stream.Dispose()
            $compressed.Dispose()
            $uncompressed.Dispose()
            break
        }
    }
    $position += 12 + $length
}
if (!$description) { throw "DMI Description not found: $Source" }

$width = 32
$height = 32
if ($description -match '(?m)^\s*width = (\d+)') { $width = [int]$Matches[1] }
if ($description -match '(?m)^\s*height = (\d+)') { $height = [int]$Matches[1] }
$sourceImage = [Drawing.Bitmap]::new($Source)
$sourceColumns = [int]($sourceImage.Width / $width)
$offset = 0
$metadata = @()
$usedNames = @{}

foreach ($match in [regex]::Matches($description, '(?ms)^state = "([^"]+)"(.*?)(?=^state = "|^# END DMI)')) {
    $name = $match.Groups[1].Value
    $properties = $match.Groups[2].Value
    $directions = 1
    $frames = 1
    if ($properties -match '(?m)^\s*dirs = (\d+)') { $directions = [int]$Matches[1] }
    if ($properties -match '(?m)^\s*frames = (\d+)') { $frames = [int]$Matches[1] }
    $count = $directions * $frames

    if (!$States -or $States -contains $name) {
        $outputName = $name
        if ($usedNames.ContainsKey($outputName)) {
            $suffix = if ($properties -match '(?m)^\s*movement = 1') { '-moving' } else { '-variant' }
            $outputName = "$name$suffix"
            $number = 2
            while ($usedNames.ContainsKey($outputName)) {
                $outputName = "$name$suffix$number"
                $number++
            }
        }
        $usedNames[$outputName] = $true
        $columns = [Math]::Min(16, $count)
        $rows = [int][Math]::Ceiling($count / $columns)
        $outputImage = [Drawing.Bitmap]::new($columns * $width, $rows * $height)
        $graphics = [Drawing.Graphics]::FromImage($outputImage)
        $graphics.Clear([Drawing.Color]::Transparent)
        for ($i = 0; $i -lt $count; $i++) {
            $sourceIndex = $offset + $i
            $sourceRect = [Drawing.Rectangle]::new(($sourceIndex % $sourceColumns) * $width,
                [int][Math]::Floor($sourceIndex / $sourceColumns) * $height, $width, $height)
            $destRect = [Drawing.Rectangle]::new(($i % $columns) * $width,
                [int][Math]::Floor($i / $columns) * $height, $width, $height)
            $graphics.DrawImage($sourceImage, $destRect, $sourceRect, [Drawing.GraphicsUnit]::Pixel)
        }
        $outputImage.Save((Join-Path $Destination "$outputName.png"), [Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose()
        $outputImage.Dispose()

        $state = [ordered]@{ name = $outputName }
        if ($directions -ne 1) { $state.directions = $directions }
        if ($frames -gt 1) {
            $delays = @(1.0) * $frames
            if ($properties -match '(?m)^\s*delay = ([^\r\n]+)') {
                $delays = @($Matches[1].Split(',') | ForEach-Object { [Math]::Round(([double]::Parse($_.Trim(), [Globalization.CultureInfo]::InvariantCulture) / 10), 3) })
            }
            $state.delays = @(for ($direction = 0; $direction -lt $directions; $direction++) { ,$delays })
        }
        $metadata += $state
        Write-Output "$outputName : $count frames"
    }
    $offset += $count
}
$sourceImage.Dispose()

$rsi = [ordered]@{
    version = 1
    license = 'CC-BY-SA-3.0'
    copyright = $Copyright
    size = [ordered]@{ x = $width; y = $height }
    states = $metadata
}
[IO.File]::WriteAllText((Join-Path $Destination 'meta.json'),
    ($rsi | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
