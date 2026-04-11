# 双机 MCPControl 接入

## 结论
- 最终目标是让 Codex 直接控制主机和副机桌面。
- `Windows-MCP.Net` 不适合这个目标，因为它是 `stdio` 本地传输。
- `MCPControl` 适合，因为它支持 `SSE` 网络传输，可以把副机桌面控制能力暴露到局域网。

## 副机一次性安装
在副机 PowerShell 里执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
powershell -ExecutionPolicy Bypass -File "C:\Tools\MCPControl\Setup-McpControlSecondary.ps1" -Port 3232
```

如果脚本还没复制到副机，可直接从共享路径运行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
powershell -ExecutionPolicy Bypass -File "\\DESKTOP-U51KJJ2\SlayTheSpire2\tools\desktop_mcp\mcpcontrol\Setup-McpControlSecondary.ps1" -Port 3232
```

## 启动服务
```powershell
powershell -ExecutionPolicy Bypass -File "C:\Tools\MCPControl\Start-McpControl.ps1" -Port 3232
```

## 说明
- 这条链会安装：Python 3.12、Node.js LTS、Visual Studio 2022 Build Tools(C++ workload)、`mcp-control`
- 会放行 TCP 3232
- 默认使用 `keysender` automation provider
- 当前先走 HTTP/SSE 内网调试；如果后续 Codex 客户端强制 HTTPS，再补 TLS 包装层
