# 东方变速器 v0.1.0

首个公开测试版，面向东方Project Windows 单机作品的低速弹幕练习。

## 下载

下载 `TouhouSpeedController-v0.1.0-win.zip` 并完整解压，然后运行 `东方变速器.exe`。不要单独复制 EXE。

## 功能

- 识别 TH06–TH20 正作及常见 `thXXX` 小数作进程名
- 自由输入 1–240 FPS，支持两位小数
- 自动判断 32 位或 64 位游戏
- 提供 60、45、30、15 FPS 快捷按钮
- 正常关闭时恢复 60 FPS

## 测试范围

- 已实测：TH11 东方地灵殿、TH18 东方虹龙洞
- 其他版本：理论支持，等待逐作测试

## 安全说明

本工具不修改游戏文件或存档，不提供联网游戏作弊功能。程序需要向选定的东方游戏进程加载开源计时组件，部分安全软件可能产生误报。

## SHA-256

```text
D4771324332138699C66593AD4D0133A4C5C26F268E8A270EA3444ECF0B25419  TouhouSpeedController-v0.1.0-win.zip
DF8469A50A59E38FBBE56253D8C12FEB07EC8848B61179D34B8B7733E68D99EA  OpenSpeedy-source-8466ca9.zip
```

## 开源许可

底层组件来自 OpenSpeedy。本 Release 同时提供对应提交 `8466ca9d9f2492db14346e58a6be8b44fc7d2a2e` 的完整源码包 `OpenSpeedy-source-8466ca9.zip`。项目按 GNU GPL v3.0 发布。

反馈：[GitHub Issues](https://github.com/StarLorentz/TouhouSpeedController/issues) · [B站主页](https://space.bilibili.com/456339208)
