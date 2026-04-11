param(
    [int]$Port = 3232,
    [switch]$RegisterStartupTask
)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sharedScript = Join-Path $scriptRoot 'Setup-McpControl.ps1'

& $sharedScript -Port $Port -InstallRoot 'J:\Tools\MCPControl' -RegisterStartupTask:$RegisterStartupTask -TaskName 'MCPControl SSE Primary'
