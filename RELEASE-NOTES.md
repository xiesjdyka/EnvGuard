## 要用程序，点这个下载

### [下载 EnvGuard-windows-x64.zip](https://github.com/xiesjdyka/EnvGuard/releases/latest/download/EnvGuard-windows-x64.zip)

下载后先解压，再双击里面的 **EnvGuard.exe**。Windows 问权限时点“是”，就会以管理员身份运行。

不清楚怎么填？双击包里的 **先看这里.html**，按说明做就行。

## 这次改了什么

- **Claude 正常更新后，不用重新选择软件。** 签名 Windows 应用包按同一应用身份和发布者自动接续新版。
- 主程序、原来确认的专属目录和勾选的服务一起接续；更新前的已识别旧进程和子进程也继续纳入手动紧急关闭。
- **旧 Claude 配置可自动接续，IP、代理和时区基准不变。** 接续和失败原因都会写入原日志。
- 普通 EXE、发布者变化、入口无法验证或未知 Cowork 虚拟机身份规则仍会明确要求确认，不仅按名字扩大范围。
- 修复旧版 HTTPS 握手默认值导致出口检测失败的问题；首次核对给冷启动连接更多时间，运行中的快速检测时限保持不变。
- 本次没有改你的代理、Chrome 或中继设置。预警与紧急关闭仍是原来的**只提醒、手动关闭**模式。
- 欢迎大家到 [讨论区](https://github.com/xiesjdyka/EnvGuard/discussions) 留言、到 [问题反馈](https://github.com/xiesjdyka/EnvGuard/issues/new/choose) 报问题，也欢迎提交代码改进。

已有用户更新时：先暂停保护的软件，退出 EnvGuard，再替换新程序。保留自己的设置和日志。

## 下载列表里的其他文件是什么？

- **SHA256SUMS.txt：** 下载校验码，不是程序，也不是使用说明。普通使用可以不下载。
- **Source code：** 开发者用的源码。只想用程序，不用下载。

EnvGuard 负责环境预警和**手动**紧急关闭；要停用时点红色按钮。账号是否受限制由服务方决定。
