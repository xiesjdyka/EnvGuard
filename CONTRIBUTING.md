# 一起来把 EnvGuard 做好

不需要会写代码。哪里看不懂、哪里不好用，告诉我们就很有帮助。

## 想留言、提建议

打开 [讨论区](https://github.com/xiesjdyka/EnvGuard/discussions)，点 **New discussion**，写下你的想法。也可以在已有讨论下面回复。

不知道在哪填代理端口、说明哪句话太绕、想适配其他软件，都欢迎说。建议带一张遮掉私人信息的截图。

## 程序出了问题

到 [问题反馈](https://github.com/xiesjdyka/EnvGuard/issues/new/choose)，选择“遇到问题”。告诉我们版本、怎么操作、发生了什么。

日志只贴与问题相关的几行；先遮掉真实 IP、用户名、电脑路径、节点链接、密码和登录信息。**不要上传完整浏览器资料、个人环境快照或真实账号测试结果。** 漏洞或敏感安全问题请按 [SECURITY.md](SECURITY.md) 私下报告，不要公开细节。

## 想改代码或说明

1. 点仓库右上角 **Fork**，复制一份到自己的 GitHub。
2. 在自己的副本里改。只是改错别字或说明，也可以直接点文件上的铅笔。
3. 做好后点 **Contribute → Open pull request**，说明改了哪里、为什么。
4. 维护者会看修改、提出建议；审核通过再合并到正式项目。

每个人都能提改进，但不能直接覆盖正式代码。提交不等于已经发布；新程序仍需构建和测试。

## 开发者怎么检查

Windows x64、.NET Framework 4.8、PowerShell 7：

```powershell
pwsh -NoProfile -File .\Build.ps1 -Test -Package
```

测试使用专门的演示进程和隔离注册表项，不能拿真实 Claude、账号或浏览器设置做自动破坏测试。两份 HTML 说明由 README 和 Chrome 指南生成，不用重复改 HTML。

涉及结束进程、网络检测或 Chrome 策略的改动，请一起补测试，写清楚权限和失败情况。请保持“异常提醒＋手动紧急关闭”的现有行为；扩大关闭范围或自动修改网络需要先讨论。

贡献按本项目 [MIT 协议](LICENSE) 提交。感谢每个愿意试用、留言、修正说明和改代码的人。
