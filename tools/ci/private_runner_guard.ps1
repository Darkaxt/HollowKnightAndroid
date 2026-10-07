# Runner-process job-start hook, copied beside the private expected-job.json.
# This runs before checkout or any workflow step; a rejected first job cannot
# execute repository code or reach the local game inputs.
$ErrorActionPreference = 'Stop'
foreach ($path in @($PSScriptRoot, $env:USERPROFILE, $env:RUNNER_WORKSPACE, $env:RUNNER_TEMP)) {
    if ($path) {
        Write-Output "::add-mask::$path"
        Write-Output ('::add-mask::' + $path.Replace('\', '/'))
    }
}
try {
    $expected = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'expected-job.json') -Raw | ConvertFrom-Json
    $names = @('GITHUB_REPOSITORY', 'GITHUB_WORKFLOW_REF', 'GITHUB_WORKFLOW_SHA',
               'GITHUB_JOB', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'GITHUB_SHA', 'GITHUB_EVENT_NAME')
    if (@($expected.PSObject.Properties).Count -ne $names.Count) { throw 'Wrong expectation schema' }
    if ($expected.GITHUB_REPOSITORY -cne 'Darkaxt/HollowKnightAndroid' -or
        $expected.GITHUB_JOB -cne 'patch-profiles' -or
        $expected.GITHUB_EVENT_NAME -cne 'workflow_dispatch' -or
        $expected.GITHUB_WORKFLOW_REF -cnotmatch '^Darkaxt/HollowKnightAndroid/\.github/workflows/release\.yml@refs/heads/' -or
        $expected.GITHUB_SHA -cnotmatch '^[0-9a-f]{40}$' -or
        $expected.GITHUB_WORKFLOW_SHA -cne $expected.GITHUB_SHA -or
        $expected.GITHUB_RUN_ID -cnotmatch '^[1-9][0-9]*$' -or
        $expected.GITHUB_RUN_ATTEMPT -cnotmatch '^[1-9][0-9]*$') { throw 'Invalid expectation' }
    foreach ($name in $names) {
        if (-not ($expected.$name -is [string]) -or
            [Environment]::GetEnvironmentVariable($name) -cne $expected.$name) {
            throw 'Unintended job context'
        }
    }
} catch {
    Write-Error 'Private compiler runner rejected an unintended or unprovisioned job.'
    exit 1
}
Write-Output 'Intended exact-source compiler job admitted before workflow execution.'
