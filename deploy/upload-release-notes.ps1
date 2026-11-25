# Uploads the release notes of every release (tag) from 0.2.0 on to the Lyrious server, published, for
# the About page: each tag's message without its first line (the version), dated as the tag. Versions
# that already have a note on that branch are left alone, so it can be run again.
#
# Run it from the repository (it asks for a site manager's username and password, used only here):
#   pwsh deploy/upload-release-notes.ps1
#   pwsh deploy/upload-release-notes.ps1 -DryRun
#
# -From <version>     the first release uploaded (default 0.2.0)
# -Branch <name>      the branch the notes are for on the server (default main: the app the server serves)
# -Draft              save them unpublished, for checking on the server first
# -DryRun             only show what would be uploaded (no login asked)
# -Server <address>   the Lyrious server (default https://www.lyrious.se)
param(
    [string]$From = "0.2.0",
    [string]$Branch = "main",
    [switch]$Draft,
    [switch]$DryRun,
    [string]$Server = "https://www.lyrious.se"
)
$ErrorActionPreference = "Stop"
# git's output (tag messages) is UTF-8.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$utf8 = New-Object Text.UTF8Encoding $false

# The releases from $From on, oldest first: version, date and notes.
$releases = @()
foreach ($tag in (& git tag --sort=v:refname)) {
    if ($tag -notmatch '^\d+\.\d+\.\d+$' -or [version]$tag -lt [version]$From) { continue }
    $message = (& git tag -l --format='%(contents)' $tag) -join "`n"
    # Without the version line, the signature and blank lines: one note per line.
    $lines = $message -split "`n" | Select-Object -Skip 1 |
        Where-Object { $_.Trim().Length -gt 0 } |
        ForEach-Object { $_.Trim() -replace '^(\*\s*)+', '' }
    $lines = @($lines | Where-Object { $_ -notmatch '^-----BEGIN' })
    if ($lines.Count -eq 0) {
        # A tag without notes: its commit's subject.
        $lines = @((& git log -1 --format=%s $tag).Trim() -replace '^(\*\s*)+', '')
    }
    $date = [DateTime]::Parse((& git tag -l --format='%(creatordate:iso-strict)' $tag))
    $releases += [pscustomobject]@{ Version = $tag; Created = $date; Text = ($lines -join "`n") }
}

if ($releases.Count -eq 0) { throw "No releases from $From on." }

Write-Host "Release notes for $($releases.Count) releases, on $Branch at $Server$(if ($Draft) { ' (unpublished)' }):"
foreach ($release in $releases) {
    Write-Host ""
    Write-Host "$($release.Version)  ($($release.Created.ToString('yyyy-MM-dd')))"
    $release.Text -split "`n" | ForEach-Object { Write-Host "  * $_" }
}
Write-Host ""

if ($DryRun) {
    Write-Host "Dry run: nothing was uploaded."
    return
}

# Log in as site manager (the password is asked hidden, used here only, and forgotten).
$userName = Read-Host "Lyrious username"
$secure = Read-Host "Password" -AsSecureString
$password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
$login = Invoke-RestMethod -Method Post -Uri "$Server/api/auth/login" -ContentType "application/json" `
    -Body (@{ username = $userName; password = $password } | ConvertTo-Json)
if (-not $login.canElevate) { throw "This account isn't a site manager, so it can't upload release notes." }
$elevated = Invoke-RestMethod -Method Post -Uri "$Server/api/auth/elevate" -ContentType "application/json" `
    -Headers @{ Authorization = "Bearer $($login.token)" } -Body (@{ password = $password } | ConvertTo-Json)
$password = $null
$headers = @{ Authorization = "Bearer $($elevated.token)" }

# The versions that have a note on the branch already: left alone.
$all = Invoke-RestMethod -Method Get -Uri "$Server/api/releasenote" -Headers $headers
$existing = @($all | ForEach-Object { $_ } |
    Where-Object { -not $_.removed -and $_.branch -eq $Branch } |
    ForEach-Object { $_.version })

$now = (Get-Date).ToString("o")
$notes = @()
foreach ($release in $releases) {
    if ($existing -contains $release.Version) {
        Write-Host "  $($release.Version): already there, left alone"
        continue
    }

    $created = $release.Created.ToString("o")
    $notes += @{
        id = [guid]::NewGuid().ToString()
        text = $release.Text
        branch = $Branch
        version = $release.Version
        published = -not $Draft
        created = $created
        createdAt = $now
        changedAt = $now
        removed = $false
    }
    Write-Host "  $($release.Version): uploading"
}

if ($notes.Count -eq 0) {
    Write-Host "Nothing to upload: every release has its note."
    return
}

$body = ConvertTo-Json -InputObject @($notes) -Depth 3
Invoke-RestMethod -Method Post -Uri "$Server/api/releasenote" -ContentType "application/json; charset=utf-8" `
    -Headers $headers -Body ($utf8.GetBytes($body)) | Out-Null
Write-Host "Uploaded $($notes.Count) release notes."
