# 东方变速器

东方Project Windows 单机作品的低速弹幕练习工具。输入目标帧率即可调整游戏速度；60 帧为原速，30 帧为 0.5 倍速，15 帧为 0.25 倍速。

> 本项目是非官方个人作品，与上海爱丽丝幻乐团及东方Project原作者不存在授权或隶属关系。

## 功能

- 识别 TH06 至 TH20 正作进程
- 兼容常见的 `thXXX` 小数作进程名
- 自动判断 32 位或 64 位游戏
- 自由输入 1–240 FPS，支持两位小数
- 提供 60、45、30、15 FPS 快捷按钮
- 关闭程序时自动恢复原速

## 测试状态

| 状态 | 作品 |
| --- | --- |
| 已实测 | TH11 东方地灵殿、TH18 东方虹龙洞 |
| 理论支持 | TH06–TH10、TH12–TH17、TH19–TH20 |

“理论支持”表示程序包含相应进程识别规则，尚未覆盖所有发行版本、汉化补丁和兼容性设置。

## 使用方法

1. 从 [Releases](https://github.com/StarLorentz/TouhouSpeedController/releases) 下载最新版 ZIP 并完整解压。
2. 启动东方游戏并进入标题画面。
3. 双击 `东方变速器.exe`，等待“应用帧率”按钮启用。
4. 选择游戏、输入目标帧率并点击“应用帧率”。
5. 点击“恢复 60 帧”回到正常速度。

不要只复制主 EXE；四个 `bridge` / `speedpatch` 文件必须和它放在同一目录。

## 安全说明

本工具不包含恶意功能，不修改游戏文件或存档，也不提供联网游戏作弊功能。程序通过向用户选定的东方游戏进程加载开源计时组件来调整时间速度，因此部分安全软件可能产生误报。请从本仓库 Releases 或作者 B站主页公布的地址下载，并核对发布页中的 SHA-256。

## 反馈

- 作者：StarLorentz / B站 [斯塔罗尔（UID 456339208）](https://space.bilibili.com/456339208)
- 问题反馈：[GitHub Issues](https://github.com/StarLorentz/TouhouSpeedController/issues)

反馈时请注明游戏编号及版本、Windows 版本、目标帧率、错误提示，以及使用的汉化补丁或其他修改。个人项目不保证及时回复或持续更新。

## 技术与许可

界面及东方进程识别代码位于 [`TouhouSpeedController.cs`](TouhouSpeedController.cs)。底层 `bridge` 和 `speedpatch` 组件来自 [OpenSpeedy](https://github.com/game1024/OpenSpeedy)，通过命名管道与本程序通信。

本项目按 [GNU General Public License v3.0](LICENSE) 发布。正式 Release 同时提供使用的 OpenSpeedy 对应源码包。

东方Project及十六夜咲夜相关权利归上海爱丽丝幻乐团及原作者所有。软件不包含游戏本体、音乐、立绘或原作数据；程序图标使用生成式 AI 制作的咲夜二次创作图像。
