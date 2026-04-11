# 双机 MCPControl 接入

## 结论
- 最终目标是让 Codex 直接控制主机和副机桌面。
- `Windows-MCP.Net` 不适合这个目标，因为它是 `stdio` 本地传输。
- `MCPControl` 适合，因为它支持 `SSE` 网络传输，可以把副机桌面控制能力暴露到局域网。
- 安装根目录统一为 `J:\Tools\MCPControl`。

## 主机一次性安装
在主机 PowerShell 里执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
powershell -ExecutionPolicy Bypass -File "K:\杀戮尖塔mod制作\STS2_mod\PVP_ParallelTurn\tools\desktop_mcp\mcpcontrol\Setup-McpControlPrimary.ps1" -Port 3232
```

## 副机一次性安装
在副机 PowerShell 里执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
powershell -ExecutionPolicy Bypass -File "\\DESKTOP-U51KJJ2\SlayTheSpire2\tools\desktop_mcp\mcpcontrol\Setup-McpControlSecondary.ps1" -Port 3233
```

如果要先复制到副机本地，也可以执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
Copy-Item -LiteralPath "\\DESKTOP-U51KJJ2\SlayTheSpire2\tools\desktop_mcp\mcpcontrol\*" -Destination "J:\Tools\MCPControl" -Recurse -Force
powershell -ExecutionPolicy Bypass -File "J:\Tools\MCPControl\Setup-McpControlSecondary.ps1" -Port 3233
```

## 启动主机服务
```powershell
powershell -ExecutionPolicy Bypass -File "J:\Tools\MCPControl\Start-McpControl.ps1" -Port 3232
```

## 启动副机服务
```powershell
powershell -ExecutionPolicy Bypass -File "J:\Tools\MCPControl\Start-McpControl.ps1" -Port 3233
```

## 说明
- 这条链优先使用 `J:` 上的便携 Node.js 22 和便携 Python 3.12.10。
- 会把 Visual Studio 2022 Build Tools 安装到 `J:\Tools\MCPControl\deps\vs2022-buildtools`。
- 会放行对应端口的 TCP 入站规则。
- 默认使用 `keysender` automation provider
- 当前先走 HTTP/SSE 内网调试；如果后续 Codex 客户端强制 HTTPS，再补 TLS 包装层
