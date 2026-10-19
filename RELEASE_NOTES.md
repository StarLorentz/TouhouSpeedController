# 练习功能基准版 · 2026-10-19

标签：`practice-baseline-2026-10-19`。本次以 Pre-release 归档已手测的功能基准，程序文件版本保持 0.1.1，保留旧版正式发布。UI 重新设计另行规划。

普通玩家下载 `TouhouSpeedController-practice-2026-10-19-win.zip`，完整解压后运行 `东方变速器.exe`。完整项目源码和对应 OpenSpeedy 源码也随发布提供；`SHA256SUMS.txt` 用于核对附件完整性。

## 本轮功能

- 保留自由输入帧率、速度预设和恢复原速。
- Ctrl+Shift+C 呼出/最小化，激活窗口时临时置顶，并补充游戏前台的按键检测。
- TH10/TH11：残机修改和无限，Power 即时同步及锁定；Bomb 共用 Power，没有独立 Bomb 数量。
- TH12/TH13/TH14/TH15/TH16/TH17/TH18/TH20：残机、Bomb、B碎片及 Power 独立/全部应用，残机和 Bomb 无限，Power 即时同步和锁定。
- TH09 仅 P1：生命修改/无限、蓄力修改/锁定和无敌。
- 无敌覆盖本机已识别的 Steam TH06 原典/新典、TH07、TH09（P1）、TH10、TH11、TH12、TH13、TH14、TH15、TH16、TH17、TH18 及 TH20；当前界面已移除实验字样。准确版本范围以 README 表格为准。
- Power 同步由游戏更新线程执行，并按本作机制重建攻击档位，不靠死亡或拾取满火力道具来刷新。

使用者已确认本轮测试通过，记录见 README.txt 和 docs/。底层检查另包含 129 项 x86 回调、58 项资源编码及 174 项窗体属性检查；不代表所有发行版和机体组合都已测过。

## 已知边界

- **TH08 仅变速可用；练习资源和无敌修改已知会闪退，请勿使用这些按钮。暂不修复。**
- 旧版 TH06 和 TH19 未接入练习功能；Steam TH06 两版和 TH07 尚无资源修改。
- 练习功能仅允许已核对的 EXE 指纹和代码入口；汉化是否兼容取决于实际运行的 EXE。
- 暂停或死亡过渡时 Power 同步会等待游戏恢复活动；普通资源 HUD 可能稍后刷新。
- 无限资源采用约 100ms 维持机制；正常关闭或切换进程时停止锁定并恢复代码补丁，当前资源数值不回滚。练习录像可能不同步。

本项目按 GNU GPL v3.0 发布；OpenSpeedy 和 thprac 的出处及许可证见随包声明。

---

# 东方变速器 v0.1.1（早期正式版）

本版本完善多种东方正作发行版本的进程识别，并适配 Steam 新上架的《东方红魔乡：原典》《东方红魔乡：新典》。

## 下载

下载 `TouhouSpeedController-v0.1.1-win.zip` 并完整解压，然后运行 `东方变速器.exe`。普通玩家只需要下载这个文件。

## 更新内容

- 新增并实测 Steam TH06 原典 `th06c.exe`
- 新增并实测 Steam TH06 新典 `th06nc.exe`
- 完善日文原版及常见汉化版主程序名称识别
- 收紧识别规则，避免把配置器、补丁器和分数转换工具误识别为游戏
- 区分程序标志与窗口展示图：程序图标使用作者头像，窗口内使用咲夜图
- 程序和说明文件版本更新为 v0.1.1

## 本机实测范围

- Steam TH06 东方红魔乡：原典、新典
- TH07 东方妖妖梦
- TH10 东方风神录
- TH11 东方地灵殿
- TH14 东方辉针城
- TH15 东方绀珠传
- TH17 东方鬼形兽
- TH18 东方虹龙洞
- TH20 东方锦上京

以上版本均已完成实际进程识别与变速组件注入验证。不同发行版本、汉化补丁及兼容性设置仍可能存在差异。

## SHA-256

```text
6867586768ED9631CFC76D7A10D490A4CB1EB17A3EAC5A66EA3E3F76EE4C3911  TouhouSpeedController-v0.1.1-win.zip
DF8469A50A59E38FBBE56253D8C12FEB07EC8848B61179D34B8B7733E68D99EA  OpenSpeedy-source-8466ca9.zip
```

## 安全与许可

本工具不修改游戏文件或存档。底层组件来自 OpenSpeedy；本 Release 同时提供对应提交 `8466ca9d9f2492db14346e58a6be8b44fc7d2a2e` 的源码包。项目按 GNU GPL v3.0 发布。

反馈：[GitHub Issues](https://github.com/StarLorentz/TouhouSpeedController/issues) · [B站主页](https://space.bilibili.com/456339208)
