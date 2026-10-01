## 要用程序，点这个下载

### [下载 EnvGuard-windows-x64.zip](https://github.com/xiesjdyka/EnvGuard/releases/latest/download/EnvGuard-windows-x64.zip)

下载后先解压，再双击里面的 **EnvGuard.exe**。Windows 问权限时点“是”，就会以管理员身份运行。

不清楚怎么填？双击包里的 **先看这里.html**，按说明做就行。

## 这次改了什么

- Chrome 说明明确移除所有 Chinese / 中文首选语言，只保留英文；不是只把英文移到第一位。
- 执行代码的入口改为：右键左下角 Windows 开始图标 → 终端（管理员）或 Windows PowerShell（管理员）。
- 补上进入解压文件夹的代码，避免管理员窗口找不到脚本；实际执行仍用 PowerShell 7。
- 完整流程仍只有三步：执行代码 → 看政策页面结果 → 移除中文、只留英文。双击 **Chrome浏览器怎么准备.html** 查看。
- 本次不改变监测与紧急关闭行为，也不会自动修改你的 Chrome。
- 欢迎大家到 [讨论区](https://github.com/xiesjdyka/EnvGuard/discussions) 留言、到 [问题反馈](https://github.com/xiesjdyka/EnvGuard/issues/new/choose) 报问题，也欢迎提交代码改进。

已有用户更新时：先暂停保护的软件，退出 EnvGuard，再替换新程序。保留自己的设置和日志。

## 下载列表里的其他文件是什么？

- **SHA256SUMS.txt：** 下载校验码，不是程序，也不是使用说明。普通使用可以不下载。
- **Source code：** 开发者用的源码。只想用程序，不用下载。

EnvGuard 负责环境预警和**手动**紧急关闭；要停用时点红色按钮。账号是否受限制由服务方决定。
