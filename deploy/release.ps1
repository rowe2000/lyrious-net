# Makes a release of Lyrious from the current branch, in one go:
#   1. asks for your Lyrious username and password and logs in as site manager (first, so a wrong
#      password stops everything before anything is changed),
#   2. commits all changes with the message (.db files, such as a SongbookPro songs.db, and the message
#      file itself are left out),
#   3. tags the commit with the next version (the last tag + 1, e.g. 0.1.15 -> 0.1.16), annotated with
#      the release notes,
#   4. rebases the Beta branch on the current branch,
#   5. pushes the current branch, Beta and the tag (pushing main deploys www.lyrious.se on Railway),
#   6. publishes the release notes on the server (Site manager / About page).
#
# The commit message: a subject line, a blank line, then "* " lines for users (these are the release
# notes) and "- " lines for developers, e.g.
#
#   Next-song button with a NEXT label
#
#   * The next-song button has a NEXT label
#   - PlaybackBar.NextLabel: rotated text in a Viewbox
#
# Run it from anywhere in the repository (this PC only runs signed scripts, hence the Bypass):
#
#   powershell -ExecutionPolicy Bypass -File deploy\release.ps1 -MessageFile message.txt
#   powershell -ExecutionPolicy Bypass -File deploy\release.ps1 -MessageFile message.txt -DryRun
#
# -Version 0.2.0      the tag instead of the last tag + 1
# -NotesBranch Main   the branch the release note is for on the server (default Beta)
# -Draft              save the release note unpublished, for checking on the server first
# -NoNotes            don't publish release notes (no login asked)
# -DryRun             only show what would be done: nothing is committed, tagged, pushed or published
# -Server <address>   the Lyrious server (default https://www.lyrious.se)
param(
    [string]$MessageFile,
    [string]$Version,
    [string]$NotesBranch = "Beta",
    [string]$BetaBranch = "Releases/Beta",
    [switch]$Draft,
    [switch]$NoNotes,
    [switch]$DryRun,
    [string]$Server = "https://www.lyrious.se"
)
$ErrorActionPreference = "Stop"
# git's output (commit messages, names) is UTF-8.
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$utf8 = New-Object Text.UTF8Encoding $false

# Runs git; stops the script when it fails.
function Invoke-Git {
    $output = & git @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed." }
    return $output
}

# A step: shown, and only done when it isn't a dry run.
function Invoke-Step([string]$What, [scriptblock]$Action) {
    if ($DryRun) {
        Write-Host "  (dry run) $What"
    }
    else {
        Write-Host "  $What"
        & $Action
    }
}

$root = (Invoke-Git rev-parse --show-toplevel) | Select-Object -First 1
Set-Location $root
$branch = (Invoke-Git rev-parse --abbrev-ref HEAD) | Select-Object -First 1
if ($branch -eq $BetaBranch -or $branch -eq "HEAD") {
    throw "Run it from the branch to release (not $branch): Beta is rebased on it."
}

# The commit message, from the file; or the last commit's when there's nothing to commit.
# What's committed: everything but .db files and the message file itself (when it's in the repository).
$exclude = @(":!*.db")
if ($MessageFile -and (Test-Path $MessageFile)) {
    # Both with forward slashes (git gives the root so; Windows paths have backslashes).
    $full = (Resolve-Path $MessageFile).Path.Replace([char]92, [char]47)
    $top = $root.Replace([char]92, [char]47).TrimEnd([char]47)
    if ($full.StartsWith($top + "/", [StringComparison]::OrdinalIgnoreCase)) {
        $exclude += ":!" + $full.Substring($top.Length + 1)
    }
}
$changes = Invoke-Git status --porcelain -- . @exclude
if ($changes) {
    if (-not $MessageFile -or -not (Test-Path $MessageFile)) {
        throw "There are changes to commit: write the commit message in a file (a subject line, a blank line, then '* ' lines for users and '- ' lines for developers) and give it with -MessageFile, e.g. -MessageFile message.txt."
    }
    $message = [IO.File]::ReadAllText((Resolve-Path $MessageFile).Path, $utf8).Trim()
}
else {
    Write-Host "Nothing to commit: releasing the last commit."
    $message = ((Invoke-Git log -1 --format=%B) -join "`n").Trim()
}
if (-not $message) { throw "The commit message is empty." }

# The release notes: the message's "* " lines (else its subject).
$lines = $message -split "`r?`n"
$notes = @($lines | Where-Object { $_ -match '^\* ' } | ForEach-Object { $_.Substring(2).Trim() })
if ($notes.Count -eq 0) { $notes = @($lines[0].Trim()) }
$notesText = $notes -join "`n"

# The version: given, or the last tag + 1 in its last part.
if (-not $Version) {
    $last = (& git describe --tags --abbrev=0 --match "*[0-9]*" 2>$null) | Select-Object -First 1
    if (-not $last) { $last = "0.0.0" }
    $parts = @(($last -replace '^v', '') -split '\.')
    $parts[$parts.Count - 1] = [string]([int]$parts[$parts.Count - 1] + 1)
    $Version = $parts -join '.'
}
if (& git tag --list $Version) { throw "The tag $Version already exists." }

Write-Host ""
Write-Host "Lyrious $Version from $branch"
Write-Host "Release notes:"
$notes | ForEach-Object { Write-Host "  - $_" }
Write-Host ""

# 1. Log in as site manager first (the password is asked hidden, used here only, and forgotten).
$token = $null
if (-not $NoNotes) {
    $userName = Read-Host "Lyrious username"
    $secure = Read-Host "Password" -AsSecureString
    $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    $login = Invoke-RestMethod -Method Post -Uri "$Server/api/auth/login" -ContentType "application/json" `
        -Body (@{ username = $userName; password = $password } | ConvertTo-Json)
    if (-not $login.canElevate) { throw "This account isn't a site manager, so it can't publish release notes." }
    $elevated = Invoke-RestMethod -Method Post -Uri "$Server/api/auth/elevate" -ContentType "application/json" `
        -Headers @{ Authorization = "Bearer $($login.token)" } -Body (@{ password = $password } | ConvertTo-Json)
    $password = $null
    $token = $elevated.token
    Write-Host "Logged in as site manager."
}

# 2. Commit (not .db files).
if ($changes) {
    Invoke-Step "commit: $($lines[0])" {
        $file = [IO.Path]::GetTempFileName()
        try {
            [IO.File]::WriteAllText($file, $message + "`n", $utf8)
            Invoke-Git add -A -- . @exclude | Out-Null
            Invoke-Git commit -q -F $file | Out-Null
        }
        finally { Remove-Item $file -ErrorAction SilentlyContinue }
    }
}

# 3. Tag, with the release notes.
Invoke-Step "tag $Version" {
    $file = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($file, "$Version`n`n$notesText`n", $utf8)
        Invoke-Git tag -a $Version -F $file | Out-Null
    }
    finally { Remove-Item $file -ErrorAction SilentlyContinue }
}

# 4. Rebase Beta on this branch (its server copy remembered, so the push only replaces that).
Invoke-Git fetch -q origin | Out-Null
$remoteBeta = (& git rev-parse --verify -q "origin/$BetaBranch") | Select-Object -First 1
Invoke-Step "rebase $BetaBranch on $branch" {
    Invoke-Git checkout -q $BetaBranch | Out-Null
    try {
        & git rebase $branch
        if ($LASTEXITCODE -ne 0) {
            & git rebase --abort
            throw "Rebasing $BetaBranch on $branch had conflicts: rebased nothing. Rebase it by hand, then push."
        }
    }
    finally { Invoke-Git checkout -q $branch | Out-Null }
}

# 5. Push this branch and the tag, then Beta (replacing only the copy seen above).
Invoke-Step "push $branch and $Version" { Invoke-Git push -q origin $branch $Version | Out-Null }
Invoke-Step "push $BetaBranch" {
    if ($remoteBeta) { Invoke-Git push -q "--force-with-lease=$($BetaBranch):$remoteBeta" origin $BetaBranch | Out-Null }
    else { Invoke-Git push -q origin $BetaBranch | Out-Null }
}

# 6. Publish the release notes.
if (-not $NoNotes) {
    Invoke-Step "publish the release notes for $Version ($NotesBranch$(if ($Draft) { ', draft' }))" {
        $now = (Get-Date).ToString("o")
        $note = @{
            id = [guid]::NewGuid().ToString()
            text = $notesText
            branch = $NotesBranch
            version = $Version
            published = -not $Draft
            created = $now
            createdAt = $now
            changedAt = $now
            removed = $false
        }
        $body = ConvertTo-Json -InputObject @($note) -Depth 3
        Invoke-RestMethod -Method Post -Uri "$Server/api/releasenote" -ContentType "application/json; charset=utf-8" `
            -Headers @{ Authorization = "Bearer $token" } -Body ($utf8.GetBytes($body)) | Out-Null
    }
}

Write-Host ""
Write-Host $(if ($DryRun) { "Dry run: nothing was changed." } else { "Released Lyrious $Version." })
