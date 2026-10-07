# Mac 桌面文件传到 Windows

如果 ZIP 已经在 Windows 上，不用再开下载服务。下面命令只在存放 ZIP 的 **Mac 终端**执行；两台电脑需要能在同一局域网互相访问。

## 查询 Mac 的 Wi-Fi / 内网 IP

```bash
networksetup -listallhardwareports
ipconfig getifaddr en0
```

`en0` 没结果时，从第一条的 Wi-Fi 对应 `Device` 找接口，再查，例如 `ipconfig getifaddr en1`。不要把代理公网出口 IP 当内网 IP。

## 临时启动下载（需要 Mac 的 Python 3，仅用于传文件）

将下面整段粘贴到 Mac 终端。它只复制这一份 ZIP 到新的临时文件夹，再绑定 Wi-Fi 内网地址提供下载，不会公开整个桌面：

```bash
(
  set -eu
  MAC_IP="$(ipconfig getifaddr en0)"
  test -n "$MAC_IP"
  SHARE_DIR="$(mktemp -d "${TMPDIR:-/tmp}/envguard-share.XXXXXX")"
  trap 'rm -r -- "$SHARE_DIR"' EXIT
  cp "$HOME/Desktop/EnvGuard_macOS_Release.zip" "$SHARE_DIR/"
  printf '\nWindows 浏览器打开：http://%s:8000/EnvGuard_macOS_Release.zip\n下载完在这里按 Ctrl+C 停止。\n\n' "$MAC_IP"
  python3 -m http.server 8000 --bind "$MAC_IP" --directory "$SHARE_DIR"
)
```

Wi-Fi 对应 `en1` 就将上面的 `en0` 换成 `en1`。端口占用可将 `8000` 改为 `8001`；Windows URL 也要同步修改。HTTP 不加密且不带认证，仅在可信局域网短时使用；系统防火墙提示时自行确认。Windows 浏览器访问局域网地址时应绕过公网代理。下载后按 **Ctrl+C** 停止服务并删除新建的临时副本，桌面原 ZIP 保留。

示例（不是已查询到的实际地址）：`http://192.168.1.23:8000/EnvGuard_macOS_Release.zip`。

没有 Python 3 时，可用 Finder 文件共享传输；运行 EnvGuard 本身不需要 Python。
