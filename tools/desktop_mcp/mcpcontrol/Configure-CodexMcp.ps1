param(
    [string]$SecondaryHost = 'DESKTOP-U51KJJ2',
    [int]$PrimaryPort = 3232,
    [int]$SecondaryPort = 3233
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) {
    Write-Host "[Codex MCP] $Message" -ForegroundColor Cyan
}

Write-Step 'Removing existing MCP entries if present'
codex mcp remove primary-desktop 2>$null | Out-Null
codex mcp remove secondary-desktop 2>$null | Out-Null

Write-Step 'Adding primary desktop MCP server'
codex mcp add primary-desktop --url "http://127.0.0.1:$PrimaryPort/mcp"

Write-Step 'Adding secondary desktop MCP server'
codex mcp add secondary-desktop --url "http://${SecondaryHost}:$SecondaryPort/mcp"

Write-Step 'Configured MCP servers'
codex mcp list
Write-Host 'Note: current interactive session may need restart/resume before new MCP servers become callable.' -ForegroundColor Yellow
