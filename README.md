# 网络认证自动登录

这是为当前认证入口 `http://192.168.6.2/ac_portal/default/pc.html` 制作的 Windows 小程序。

## 使用

1. 从 [Releases](https://github.com/lunaiii/portal-auto-login/releases/latest) 下载 `PortalAutoLogin.exe` 并双击。第一次运行会显示设置窗口。
2. 在**自己的电脑上**输入认证账号和密码，点击“保存并启用”。无需向任何人发送密码。
3. 保持“登录 Windows 后自动运行”勾选，之后程序会在托盘后台运行。它每 20 秒检查一次网络；遇到这个认证入口才会自动提交登录。
4. 双击托盘图标可以重新打开设置；右键托盘图标可立即检查或退出。

当前已经联网时，状态会显示“已联网，无需登录”。下次认证失效或重新开机后，程序才会实际提交密码。若登录被服务器拒绝，程序会暂停重试并弹出提示，避免持续尝试错误密码；改好密码后点击“保存并启用”即可继续。

账号保存在 `%LOCALAPPDATA%\PortalAutoLogin\settings.json`，密码保存在当前 Windows 用户的**凭据管理器**中，条目名称是 `PortalAutoLogin:192.168.6.2`。开机自启使用当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不需要管理员权限。取消勾选自启并保存可关闭自启；退出程序可停止当前会话的后台检测。

认证页面及登录接口是 HTTP。该页面用带时间戳密钥的 RC4 处理密码，但密钥与密文一起通过 HTTP 发送，无法提供 HTTPS 那样的传输保护。建议只在可信任的本地网络使用；有条件时请网络管理员提供 HTTPS 认证入口。

源码在 [`PortalAutoLogin/`](PortalAutoLogin/)。使用 .NET 10 SDK 可以重新构建：

```powershell
dotnet publish .\PortalAutoLogin\PortalAutoLogin.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\dist
```

这个程序只针对当前页面脚本的登录协议。网络管理员更换认证系统后，程序会在页面特征不匹配时停止提交密码，并提示通过浏览器登录。

## 许可证

本项目以 GNU General Public License v3.0 only（GPL-3.0-only）发布，完整条款见 [`LICENSE`](LICENSE)。
