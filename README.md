# 🧰 小辰CAD工具箱

> **AutoCAD 效率增强插件 — 七大专业图库 · 缩略图预览 · 连续插入 · 一次安装永久自动加载**

小辰CAD工具箱是一款专为 AutoCAD 设计的功能增强插件，帮助工程师和技术人员告别重复性绘图操作。内置建筑、结构、暖通、电气、给排水、装饰、通用七大专业图库，配合图库管理器、选图入库、搜索等实用功能，让图块调用从"翻文件夹、手动输入路径"变成"双击即插入"。

---

## ✨ 核心特性

- **📚 七大专业图库** — 开箱即用，覆盖日常绘图高频图块
- **🖼️ 缩略图预览** — 看图选块，不再盲猜文件名
- **⚡ 连续插入** — 同类型图块连点连插，效率翻倍
- **📥 选图入库** — 图纸上的好块一键收进自己的图库，日积月累
- **🔍 图块搜索** — 关键词秒定位，不用翻目录
- **🔌 零侵入自动加载** — 装一次，每次开 AutoCAD 自动就绪
- **🎯 经典菜单栏** — 兼容 AutoCAD 2021–2027 全系（简体/英文均可）
- **🚫 无冲突** — 与源泉设计、天正等其他插件互不干扰

## 📦 压缩包里有什么

```
XcTools_v1.0.0/
├── net8/                          ← AutoCAD 2025 / 2026 / 2027 用
│   ├── XcTools.dll
│   ├── XcTools.deps.json
│   └── XcTools.runtimeconfig.json
├── net48/                         ← AutoCAD 2021 / 2022 / 2023 / 2024 用
│   └── XcTools.dll
├── library/                       ← 七大专业图库
├── XcTools.lsp                    ← AutoLISP 加载器（双版本自动选 DLL）
├── 安装小辰CAD工具箱.bat           ← 一键注册自动加载（推荐）
└── favicon.ico
```

## 🖥️ 系统要求

| 项目 | 要求 |
|------|------|
| AutoCAD | 2021 – 2027（简体中文或英文版） |
| 操作系统 | Windows 10 / 11 |
| .NET | 2025/2026 需 .NET 8.0 Runtime（AutoCAD 已自带） |

## 🚀 安装（三选一，均支持永久自动加载）

### 方式一：一键安装 ⭐ 推荐

1. 把整个 `XcTools_v1.0.0` 文件夹解压到一个**固定目录**（例如 `D:\XcTools`，不要放桌面或需要管理员权限的系统目录）
2. 双击 **`安装小辰CAD工具箱.bat`**
3. 重启 AutoCAD → 自动加载完成 ✅

### 方式二：APPLOAD

启动 AutoCAD → 输入 `APPLOAD` → 选择 `XcTools.lsp` → 点"加载"。

### 方式三：拖入

把 `XcTools.lsp` 直接拖到 AutoCAD 绘图区。个别版本首次会弹一次定位窗口，选中本文件即可，之后永久自动。

> 💡 三种方式都会自动把加载语句写入本机所有版本 AutoCAD 的 `acad.lsp`，之后每次启动任意版本（2021–2026）都会自动加载。

## 🗑️ 卸载

1. 在 AutoCAD 命令行输入 `XCTUNSET`（自动移除开机加载项）
2. 关闭 AutoCAD，删除插件文件夹即可

## 📋 命令列表

在命令行输入以下命令，或使用菜单栏「小辰CAD工具箱」：

| 命令 | 功能 |
|------|------|
| `XTK` | 图库管理器（浏览、预览、双击插入） |
| `XCI` | 插入图块 |
| `XCM` | 连续插入 |
| `XAD` | 选图入库（把当前选中的图块保存到图库） |
| `XCFD` | 搜索图块 |
| `XCRF` | 刷新缩略图 |
| `XCF` | 新建分类 |
| `XCREN` | 重命名 |
| `XCDEL` | 删除 |
| `XCH` | 显示命令列表 |
| `XSETLIBPATH` | 设置图库路径 |
| `XLIBPATH` | 查看当前图库路径 |
| `XLOADUI` | 手动加载菜单栏界面 |
| `XCTLOAD` | 重新加载插件 |
| `XCTUNSET` | 移除开机自动加载（卸载用） |

## 🗂️ 默认图库结构

```
library/
├── 建筑(ARCH)
├── 结构(STR)
├── 暖通(HVAC)
├── 电气(ELEC)
├── 给排水(PL)
├── 装饰(DECO)
└── 通用(GEN)
```

- 双击图库中的图形即可插入当前图纸
- 用 `XAD` 可以把图纸上选中的图块存入图库，日积月累形成专属图库
- 图库路径可通过 `XSETLIBPATH` 修改（例如指向公司服务器的共享图库）

## ❓ 常见问题

**Q：启动后菜单栏没有「小辰CAD工具箱」？**
在命令行输入 `XLOADUI` 手动加载一次，之后会永久生效。

**Q：重启 AutoCAD 后没有自动加载？**
确认没有把文件夹放在桌面、下载等临时位置；确认杀毒软件没有拦截 `XcTools.lsp` / `XcTools.dll`（建议将插件目录加入杀软白名单）。最简单的修复：双击 `安装小辰CAD工具箱.bat` 重新注册一次。

**Q：AutoCAD 2021–2024 提示"未找到 XcTools.dll"？**
说明发布包缺少 `net48` 文件夹（2025/2026/2027 用 `net8`，2021–2024 用 `net48`），请确认拿到了完整发布包。

**Q：缩略图不显示？**
输入 `XCRF` 刷新缩略图缓存。

**Q：装了源泉设计等其他插件有冲突吗？**
互不冲突，可同时使用。

---

<p align="center">
  小辰CAD工具箱 — 让 CAD 设计更高效<br>
  <a href="https://qun.qq.com/universal-share/share?ac=1&authKey=SGMhgdWVYZFnGfh5Y5%2BOeF56UNrHt5rAq6Knrk6LFF7nYnbkdY%2Fit7vMXl5JJIPp&busi_data=eyJncm91cENvZGUiOiI5NjQwNjY2NTQiLCJ0b2tlbiI6Ik9Hakg2b29GdU9JL1pnb1B4V0xPMHdQRmRsNlNZeUVNWmtqYVZmRS91UXNMeURxOEJIU2twOVN4N0lGcUJaUTMiLCJ1aW4iOiIyMDYyMDc3NTA1In0%3D&data=pO9xuT3mYefsfhSUVz60x1rgZZkHV-Zrxf1UlxleR2_igJHczc1HDKZZLqNBnxDsdSKRziHIa1sJeNgMGwYSgw&svctype=4&tempid=h5_group_info">QQ 交流群：964066654</a>
</p>
