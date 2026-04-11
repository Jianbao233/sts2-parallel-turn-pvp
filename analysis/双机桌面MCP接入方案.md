# 双机桌面 MCP 接入方案

## 目标
- 让 Codex 最终能直接控制主机和副机两台 Windows 机器，执行完整的 STS2 PvP 回归测试。

## 选型结论
- 排除 `Windows-MCP.Net`
  - 原因：该项目当前是 `stdio` 本地传输，适合单机，不适合把副机桌面能力直接暴露到当前会话。
- 采用 `MCPControl`
  - 原因：该项目支持 `SSE` 网络传输，允许在副机运行桌面控制服务，再由主机侧 MCP 客户端直接接入。

## 当前确定的接入路径
1. 主机运行一个 `MCPControl` 实例
2. 副机运行一个 `MCPControl` 实例
3. 两边都监听固定端口
   - 主机建议：`3232`
   - 副机建议：`3233`
4. 两边都开放对应 TCP 入站规则
5. Codex 后续会话读取 MCP 配置后，分别把两台机器作为两个 MCP server 接入

## 为什么这是当前唯一可行的“双机直连”方案
1. 目标不是“在两台机器上装自动化脚本”
2. 目标是“让我当前这边直接把副机桌面当工具来用”
3. 要满足这一点，必须是网络传输型 MCP server
4. `Windows-MCP.Net` 当前做不到这一点，`MCPControl` 能做到

## 技术栈要求
### 每台机器
- Python 3.12
- Node.js LTS
- Visual Studio 2022 Build Tools
  - 包含 `Desktop development with C++` / `VC Tools`
- `mcp-control` 全局安装

### 为什么必须装 C++ Build Tools
- `mcp-control` 依赖 `keysender`
- `keysender` 是原生模块，安装时会走 `node-gyp`
- 没有可用的 VS 2022 C++ 工具链，安装会失败

## 当前限制
1. 这次会话本身不会热加载新的 MCP server
2. 所以现在能做的是：
   - 把双机安装脚本、部署路径、工作流先落好
   - 让你在副机执行一次安装
3. 等 MCP 配置加进 Codex 并重开/重载会话后，我才能真正直接调用这两台机器

## 当前已落地文件
- [Setup-McpControlSecondary.ps1](K:\杀戮尖塔mod制作\STS2_mod\PVP_ParallelTurn\tools\desktop_mcp\mcpcontrol\Setup-McpControlSecondary.ps1)
- [README.md](K:\杀戮尖塔mod制作\STS2_mod\PVP_ParallelTurn\tools\desktop_mcp\mcpcontrol\README.md)

## 副机共享目录已部署
- `\\DESKTOP-U51KJJ2\SlayTheSpire2\tools\desktop_mcp\mcpcontrol`

## 你下一步要做的事
在副机 PowerShell 执行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
powershell -ExecutionPolicy Bypass -File "\\DESKTOP-U51KJJ2\SlayTheSpire2\tools\desktop_mcp\mcpcontrol\Setup-McpControlSecondary.ps1" -Port 3233
```

安装完成后启动服务：

```powershell
powershell -ExecutionPolicy Bypass -File "C:\Tools\MCPControl\Start-McpControl.ps1" -Port 3233
```

## 后续我这边接的配置方向
后续会在 Codex MCP 配置中增加两个网络型 server：
- `primary-desktop` -> `http://<主机IP>:3232/mcp`
- `secondary-desktop` -> `http://<副机IP>:3233/mcp`

如果客户端强制 HTTPS，再补证书和 TLS 反向代理层。
