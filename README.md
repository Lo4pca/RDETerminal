# RDETerminal

**RDETerminal** 是一个为游戏《Rhythm Doctor》开发的 BepInEx 5 插件，它在游戏内置编辑器界面上方创建了一个可执行 C# 代码的终端面板，方便关卡制作者以编程方式批量修改关卡事件。

该插件的代码补全与执行能力由 **Roslyn** 提供支持。

## 主要功能

- **C# 终端面板**：在游戏编辑器中按 `F1` 即可呼出，支持代码补全和实时执行。
- **关卡事件编程**：通过 `game.Capture()` 和 `game.Apply()` 等 API，可以批量读取、修改和写回关卡事件。
- **变换与查询**：内置了针对 `FloatingText` 等事件的变换函数（如文本拆分、随机化位置/角度），也支持自定义查询过滤。
- **用户脚本热加载**：在 `Scripts` 文件夹中放置 `.cs` 文件，点击 **“Reload”** 即可在不重启游戏的情况下加载自定义类和方法，并自动集成到补全系统中。

## 安装方法

### 前置要求

1. 已安装 **BepInEx 5**。
2. 《Rhythm Doctor》游戏本体。

### 安装步骤

1. **下载插件压缩包**：从 Release 页面获取 `RDETerminal.zip`，解压后得到 `RDETerminal.dll` 及若干依赖 `.dll` 文件。

2. **复制到 BepInEx 插件目录**：  
   将解压后的所有文件放入游戏根目录下的 `BepInEx/plugins` 文件夹中（若没有该文件夹，请先正确安装 BepInEx）。

3. **启动游戏**：  
   正常启动游戏，BepInEx 会自动加载插件。进入编辑器界面后，按下 `F1` 即可呼出终端面板。

> **提示**：插件首次启动时，会在 `BepInEx/plugins/RDETerminal/` 目录下生成必要的文件。所有用户自定义脚本应放在 `RDETerminal.dll` 同目录的 `Scripts` 文件夹中。

## 快速上手

打开终端面板后，尝试执行以下代码：

```csharp
// 捕获当前关卡的所有事件
var level = game.Capture();

// 将所有 FloatingText 事件的文本按字符拆分，并生成逐字显示事件
level = FloatingTextTransforms.SplitAndAdvanceTextsWithOffset(
    level,
    evt => EventQueries.BarAtLeast(evt, 1),
    0.2
);

// 应用修改回编辑器
game.Apply(level);
```

## 文档

完整的使用指南、API 参考和项目架构说明，请参阅仓库中的 [DOC.md](DOC.md) 文件。

## 已知限制

- **热重载内存累积**：由于 Mono 运行时无法卸载程序集，每次点击“Reload”都会在内存中保留旧的脚本程序集。频繁重载可能导致内存占用上升，建议在开发阶段适度使用，必要时重启游戏释放内存。

## 致谢

- **Random** – 项目创意发起者与实机测试者，在开发过程中提供了大量宝贵建议。
- ChatGPT、Claude、DeepSeek – 分别参与了架构设计、代码重构与文档编写。

## 参考项目

- [BepInEx 官方教程](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/index.html)
- [Harmony 官方教程](https://harmony.pardeike.net/articles/intro.html)
- [RDEditorPlus](https://github.com/9thCore/RDEditorPlus)
- [Roslyn 补全功能教程](https://www.strathweb.com/2018/12/using-roslyn-c-completion-service-programmatically/)
- [Roslyn 补全功能案例](https://github.com/roslynpad/roslynpad)