# NetPulse

NetPulse 是一个原生 Windows 网络测速 MVP，无第三方运行时或 NuGet 依赖。界面基于 WPF，测速核心使用 `HttpClient`。

## 已实现

- 空载延迟与相邻样本抖动
- 四路并发下载测速
- 三路并发上传测速
- 实时进度、实时速率与取消
- 可修改测速服务地址
- 最近 12 次测试结果的本机记录
- 单文件 `NetPulse.exe`

## 运行

双击 `NetPulse.exe`。默认使用 `https://speed.cloudflare.com` 的 `__down` 和 `__up` 接口。

历史记录保存在：

```text
%LOCALAPPDATA%\NetPulse\history.tsv
```

## 重新构建

在 Windows PowerShell 中运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

构建脚本调用 Windows 自带的 .NET Framework 编译器，不需要安装 Visual Studio 或 .NET SDK。

## 自建测速节点

当前客户端约定测速服务提供：

- `GET /__down?bytes=N`：返回指定体积或足够大的不可压缩响应
- `POST /__up`：完整接收请求体并返回 2xx

生产环境建议部署多个地区、多个运营商的 HTTPS 节点，并增加节点发现、健康检查与签名配置。

## 测量说明

测速结果表示客户端到所选测速节点的端到端表现，不等同于运营商线路的理论带宽。Wi-Fi、代理、VPN、服务器出口、CPU 调度以及测试时长都会影响结果。

默认节点由 Cloudflare 提供。测速会向该节点传输测试流量；NetPulse 本身只在本机保存结果，不上传历史记录。
