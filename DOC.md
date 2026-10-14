# DOC

## 简介

`Rhythm Doctor Editor Terminal`是一个针对游戏"Rhythm Doctor"的BepInEx 5 Unity插件。该插件用Harmony插入自定义代码，在游戏内置的编辑器界面上方创建了一个执行代码的终端面板，允许用户调用项目内部创建的函数修改关卡事件。终端的代码执行与代码补全功能均由Roslyn支持

本项目的代码由AI生成，人工校验（然而我什么也没校验出来）。各AI参与的部分如下：
- ChatGPT：项目架构与具体实现
- Claude：代码重构
- DeepSeek：杂项问题解答与文档编写

项目开发时参考了如下资料：
- BepInEx官方教程： https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/index.html
- Harmony官方教程： https://harmony.pardeike.net/articles/intro.html
- BepInEx 5插件案例： https://github.com/9thCore/RDEditorPlus
- Roslyn补全功能教程： https://www.strathweb.com/2018/12/using-roslyn-c-completion-service-programmatically
- Roslyn补全功能使用案例：
    - https://github.com/roslynpad/roslynpad/blob/main/src/RoslynPad.Editor.Windows/Shared/RoslynCodeEditorCompletionProvider.cs
    - https://github.com/dotnet/roslyn/issues/23447

`Random`提出了项目创意并负责实机测试，他在项目开发过程中给出了诸多建议

## 使用方法

### 1. 面板操作

- **开关面板**：在编辑器界面按 `F1` 键。
- **执行代码**：在输入框聚焦时按 `Ctrl+Enter`。
- **命令历史**：聚焦输入框后按 **上/下箭头** 切换已执行过的命令。
  *若补全面板已弹出，则上下箭头改为切换补全项，`Enter` 确认补全。*
- **拖拽面板**：按住标题栏 `RDETerminal` 拖动。

> 相关逻辑见 `UI/TerminalWindow.cs` 的 `Update()` 方法。

### 2. 代码编辑行为

输入框内置了一套贴近 VS Code 的编辑规则，日常输入无需手动对齐或补括号：

| 操作 | 行为 |
|------|------|
| **自动缩进** | 换行时新行沿用上一行的缩进；若上一行以 `(`、`[`、`{` 结尾，则多缩进一级。若紧随光标的是匹配的闭括号（如 `(\|)`），闭括号会被移动到单独一行，光标停在中间缩进位置。 |
| **括号自动补全** | 输入 `(`、`[`、`{` 时自动插入匹配的闭括号，光标停在中间。仅在下一个字符是空白、行尾或 `;:.,=}])>` 之一时触发，且不会在字符串或 `//` 注释内触发。 |
| **引号自动补全** | 输入 `"` 或 `'` 时自动补全配对的引号。仅在作为“开引号”时触发（前一个字符不是字母或数字，避免 `don't` 被误触），且不在字符串、字符字面量或注释内触发。 |
| **跳过闭括号/引号** | 在已存在的闭括号或闭引号前输入相同字符时，直接跳过而不重复输入，避免产生 `))` 或 `""`。 |
| **包裹选区** | 选中一段文本后输入 `(`、`[`、`{`、`"` 或 `'`，会用配对符号包裹选区，且选区仍保持选中，可继续叠加包裹。 |
| **删除配对符号** | 光标位于空的配对中间（如 `(\|)`）时按 `Backspace`，会同时删除两个字符。 |
| **缩进键** | `Tab` 缩进当前行或选中的多行（或按 `Shift+Tab` 反缩进）。若无选区，`Tab` 将光标推进到下一个缩进停止点。 |
| **反缩进停止点** | 在行首缩进区按 `Backspace`，一次删除到上一个缩进停止点（4 个空格），而不是一次一个。 |

> 编辑规则实现在 `UI/TerminalInputEditing.cs` 中，纯 C# 逻辑，不依赖 Unity 类型。`UI/TerminalWindow.cs` 负责在 `Update` 中检测“刚刚发生了什么”，调用这些规则并应用结果。由于只处理“刚输入/删除一个字符”或“用字符替换选区”的场景，粘贴、历史召回与补全提交都不会被这些规则改动。

### 3. 快速上手示例

捕获当前关卡的所有事件：

```c#
var l = game.Capture();
```

使用变换函数修改事件（例如 `FloatingTextTransforms.SplitAndAdvanceTextsWithOffset`）：

```c#
l = FloatingTextTransforms.SplitAndAdvanceTextsWithOffset(
    l,
    evt => EventQueries.BarAtLeast(evt, 5),
    0.2
);
```

此操作会：
- 筛选 `bar ≥ 5` 的 `FloatingText` 事件；
- 将其 `text` 字段中每个字符间插入 `/`；
- 自动插入对应数量的 `AdvanceText` 事件，间隔 0.2 beat（模拟对话框逐字显示效果）。

> **注意**：以上变换仅修改内存中的 `LevelDocument` 对象，不会影响编辑器中的实际事件。

将修改应用到编辑器：

```c#
game.Apply(l);
```

### 4. 脚本 API 参考

#### 全局变量（`ScriptGlobals`）

| 变量 | 类型 | 说明 |
|------|------|------|
| `session` | `NotebookSession` | 会话管理器，包含已执行单元格、变量存储等。 |
| `vars` | `VarApi` | 变量操作：`Set(name, value)`、`TryGet(name, out value)`、`Get<T>(name)`。 |
| `events` | `EventApi` | 事件操作：`Sel()` 获取当前编辑器中选中的事件集。 |
| `sel` | `Func<EventSet>` | 等价于 `() => events.Sel()`，可简写为 `sel()`。 |
| `game` | `GameApi` | 关卡操作：`Capture()`、`Apply()`、`Current` 属性。 |
| `editor` | `EditorAdapter` | 编辑器底层操作：创建事件、获取选中事件、节奏录制等。 |
| `ans` | `object` | 存储上一个脚本单元的返回值。 |
| `Level` | `LevelDocument` | `session.WorkingLevel` 的别名，用于快速读写当前关卡快照。 |

#### `session` 常用成员

- `Cells` – 已执行过的 `NotebookCell` 列表。
- `WorkingLevel` – 当前正在编辑的关卡快照（`game.Capture()` 的返回值，也是 `game.Apply()` 的默认参数）。
- `Print(object value)` – 将值加入输出缓冲区。
- `ConsumePrintedOutput()` – 获取并清空输出缓冲区内容。

#### `game` 常用成员

- `Current` – `session.WorkingLevel` 的别名。
- `Capture()` – 捕获当前编辑器关卡，生成 `LevelDocument`，并自动存入 `session.WorkingLevel`。
- `Apply(LevelDocument level = null)` – 将 `level` 应用到编辑器；若未指定，则应用 `session.WorkingLevel`。

#### `editor` 常用成员

- `GetSelectedEvents()` – 返回当前选中的事件集（`events.Sel()` 的底层实现）。
- `CreateEvents` 系列方法 – 批量创建事件。  
  常用签名：
  ```csharp
  EventSet CreateEvents(
      string eventTypeName,
      float spacing,
      int number,
      int numTracks,
      int startY = 0
  )
  ```
  - 若调用前**有选中事件**，则从选中事件的位置开始向后排列新事件（忽略 `startY`）。
  - 否则从第 1 小节第 1 拍、纵坐标 `startY` 处开始排列。
  - 事件间隔 `spacing`（拍），分布在 `numTracks` 个轨道（纵坐标依次递增，循环使用）。
- `RecordTaps(KeyCode tapKey = KeyCode.Space, KeyCode stopKey = KeyCode.F12)` – 启动交互式节奏录制，返回 `Task<TimeSequence>`。用户按下 `tapKey` 记录拍点，按下 `stopKey` 结束。  
  - 首次敲击若播放尚未开始，会尝试自动开始播放；此时首个敲击对应时间偏移 `0`。
  - 录制期间游戏自身的输入路径被临时屏蔽，避免敲击同时触发游戏内操作。
- `ToggleDev()` – 切换调试模式开关。开启后，在游戏界面输入 `despacit0` 即可打开/关闭调试界面。
- `SetDev(bool value)` – 显式设置调试模式开关。
- `RestoreGameSetting()` – 恢复调试模式的原始游戏设置（撤销 `ToggleDev` / `SetDev` 的覆盖）。

#### 查询函数（`Domain.Queries.EventQueries`）

| 函数 | 说明 |
|------|------|
| `TypeIs(evt, typeName)` | 判断事件类型是否匹配（不区分大小写）。 |
| `BarAtLeast(evt, bar)` | 事件所在小节 ≥ `bar`。 |
| `BeatAtMost(evt, beat)` | 事件所在拍 ≤ `beat`。 |
| `IsEventInRowTab(evt)` | 判断事件是否属于 Row 标签页（`AddOneshotBeat`、`AddClassicBeat`、`AddFreeTimeBeat`、`PulseFreeTimeBeat`、`SetRowXs`）。 |
| `AllEvents(evt)` | 恒返回 `true`，作为变换函数的默认过滤器。 |

#### 变换函数（`Domain.Transforms`）

**通用变换（`Common.EventTransforms`）**

| 函数 | 说明 |
|------|------|
| `SetEventSpacingStartFrom(level, startBar, startBeat, spacing, filter)` | 对满足 `filter` 的事件重新设置时间位置，从 `(startBar, startBeat)` 开始，每个事件间隔 `spacing` 拍。 |
| `SetEventSpacingStartFrom(level, startBar, startBeat, Sequence sequence, filter)` | 用给定的**拍域序列**为满足 `filter` 的事件重新设置位置。序列偏移被解释为全局四分音符偏移，再经 CPB 表换算为小节/拍。 |
| `SetEventSpacingStartFrom(level, startBar, startBeat, TimeSequence sequence, filter)` | 用给定的**时域序列**（秒）为满足 `filter` 的事件重新设置位置。会综合考虑关卡中所有 BPM 和 CPB 变化。 |

**Action 类事件专用变换（`Action.FloatingTextTransforms`）**

| 函数 | 说明 |
|------|------|
| `SplitAndAdvanceTextsWithOffset(level, filter, offset)` | 将满足条件的 `FloatingText` 文本按字符分割（插入 `/`），并在每个字符后生成 `AdvanceText` 事件，间隔 `offset` 拍。`AdvanceText` 的位置通过 `LevelTimingMap` 计算，能正确响应关卡中途的 BPM/CPB 变化。 |
| `RandomizeTextsAnglesPositions(level, minX, maxX, minY, maxY, filter, rng)` | 随机改变满足条件的 `FloatingText` 的显示角度和位置。 |
| `SetTextFontSize(level, newSize, filter)` | 修改满足条件的 `FloatingText` 的字体大小。 |
| `SetTextDuration(level, newDuration, filter)` | 修改满足条件的 `FloatingText` 的持续时长（淡出速率）。 |

**Row 类事件专用变换（`Row.BeatTransforms`）**

| 函数 | 说明 |
|------|------|
| `TransferBeatsFrom(level, fromRow, toRow, startBar, startBeat)` | 将 `fromRow` 上大于等于 `(startBar, startBeat)` 的拍子转移到 `toRow`。内部通过“删除原事件 + 创建新事件”实现，因为事件的父变换依赖 room id，单纯修改 Row 字段会出错。 |

> 所有变换函数均返回新的 `LevelDocument` 实例，原对象保持不变，便于链式调用。

### 5. 用户脚本热加载

#### 脚本存放

在 `RDETerminal.dll` 同目录下创建 `Scripts` 文件夹，将你编写的 `.cs` 文件放入其中。插件启动时会自动扫描并编译该文件夹下的所有 C# 文件（支持子目录）。

#### 操作方式

- **首次加载**：打开终端面板时自动完成，无需额外操作。
- **热重载**：当你修改了 `Scripts` 文件夹中的任意 `.cs` 文件后，点击终端面板右下角的 **“Reload”** 按钮（与 `Reset` 按钮并列），即可重新编译所有脚本并更新补全上下文。

#### 脚本可定义的内容

用户脚本中可以定义：
- 公共类、接口、枚举
- 公共静态方法和实例方法
- 扩展方法（需放在静态类中）

编译成功后，这些类型、命名空间和成员会自动被终端补全系统识别，你可以在交互式输入框中直接使用它们。

示例：

```csharp
// Scripts/MyHelpers.cs
using System;
using System.Collections.Generic;

public static class MyHelpers
{
    public static string Greet(string name) => $"Hello, {name}!";
}
```

重载后，在终端中输入 `MyHelpers.Greet("Alice")` 即可调用，补全也会正常出现。

#### 重要提醒：内存累积问题

由于插件运行在 Unity 的 Mono 环境下，**已加载的程序集无法被卸载**。每次点击 `Reload` 都会将新编译的脚本程序集加载到内存中，而旧版本的程序集仍会保留，无法释放。

这意味着：
- 频繁重载会导致内存中堆积多个历史版本的程序集。
- 内存占用会逐渐增加，可能最终影响游戏性能或触发内存溢出。

**建议：**
- 开发阶段可以正常使用热重载，但不要过于频繁。
- 如果感觉游戏卡顿或内存占用过高，请**重启游戏**以释放所有累积的程序集。

### 6. 节奏录制与时序变换

插件提供了一套从“物理敲击”到“关卡事件位置”的完整工具链，适用于需要按真实节奏摆放事件的场景。

#### 快速上手

录制一段敲击节奏：

```c#
// 空格键记录拍点，F12 结束
TimeSequence taps = await editor.RecordTaps(KeyCode.Space, KeyCode.F12);
```

将这段时序转换为拍偏移，并把选中事件按该节奏重新排列：

```c#
var l = game.Capture();
l = EventTransforms.SetEventSpacingStartFrom(
    l,
    1, 1f,          // 从第 1 小节第 1 拍开始
    taps,           // 时域序列
    evt => evt.Type == "FloatingText"
);
game.Apply(l);
```

#### 核心概念：`Sequence` 与 `TimeSequence`

| 类型 | 域 | 说明 |
|------|------|------|
| `TimeSequence` | 时间（秒） | 描述每个元素的**经过时间偏移**。与 BPM、小节、拍无关，是物理敲击的自然表示。 |
| `Sequence` | 拍 | 描述每个元素的**拍偏移**。与时间无关，是事件在时间线上的自然表示。 |
| `LevelTimingMap` | 桥梁 | 在两者之间转换，并负责 `小节/拍 ↔ 绝对拍` 的换算。 |

`TimeSequence.ToSequence(level, startBar, startBeat)` 可以将时域序列按指定关卡的速度曲线转换为拍域序列。

#### 重要语义

- **时域序列独立于速度**：`TimeSequence` 记录的是真实秒数，不受关卡 BPM 变化影响。
- **拍域序列独立于时间**：`Sequence` 记录的是拍数，不受播放速度影响。
- **转换是分段线性的**：`LevelTimingMap` 会正确处理关卡中途的 BPM 与 CPB 变化，不会用单一速率近似整个关卡。
- **变换返回新对象**：所有变换函数均返回新的 `LevelDocument`，原对象保持不变。

## 项目架构

项目分为五个核心层：

### 1. Adapters
封装与游戏编辑器及事件系统的底层交互。

- `EditorAdapter.cs` – 包装编辑器内置方法，提供事件创建、选中事件获取、节奏录制、调试模式控制等操作。  
- `EditorPositionSource.cs` – 读取编辑器实时播放时钟（以名义秒为单位），返回 `scrConductor.visualPos`。多次采样之间的差值不受 BPM 变化影响。  
- `GameLevelBridge.cs` – 实现关卡快照的捕获与应用（全量同步）。  
- `ReflectionGameEventBridge.cs` – 基于反射与表达式树，实现单个事件快照的捕获与应用（增量更新）。  
- `ReflectionUtil.cs` – 反射操作的通用辅助函数。  
- `TapSequenceRecorder.cs` – 交互式时序录制器。以 `MonoBehaviour` 形式运行，负责按键检测、状态机推进（Armed → StartingPlayback → Recording），最终产出 `TimeSequence`。  
- `TapRecordingInputSuppression.cs` – 静态输入抑制开关。录制期间临时屏蔽游戏自身的 `RDInput` 路径，避免敲击同时触发游戏内操作，录制结束后的下一帧恢复。

### 2. Domain
定义数据模型、核心类型以及脚本可调用的查询/变换函数。

- **Abstractions** – 底层接口定义。  
- **Core** – 核心数据类型：  
  - `ApplyResults.cs` – 应用快照后的执行结果统计。  
  - `EventFieldNames.cs` / `EventTypeNames.cs` – 事件字段名与类型名常量。  
  - `LevelDocument.cs` – 多个事件快照的容器，用于关卡级同步。  
  - `LevelEventSnapshot.cs` – 单个事件快照，携带变化追踪。  
  - `SnapshotValueCloner.cs` / `SnapshotValueComparer.cs` – 快照的深度克隆与相等比较。  
  - `LevelTimingMap.cs` – 将音乐位置（小节/拍）与名义经过时间（秒）互相转换。内部维护两张分段表（CPB 表与 Tempo 表），通过“绝对拍”坐标连接，正确处理关卡中途的 BPM 与 CPB 变化。不依赖活动的 `scrConductor`，编辑器停止时也可使用。  
  - `Sequence.cs` – 拍域序列，描述每个元素的拍偏移。支持 `Skip`、`Rebase` 等视图操作。  
  - `TimeSequence.cs` – 时域序列，描述每个元素的经过时间偏移（秒）。与节拍、小节、Unity 无关，是物理敲击的自然表示。支持 `Skip`、`Rebase`、`FromOffsets`、`Generate`、`ConstantSpacing`、`FromIntervals`，以及 `ToSequence` 转换为拍域序列。  
- **Queries** – 供脚本使用的过滤函数（如按类型、位置筛选）。  
- **Transforms** – 供脚本使用的变换函数：  
  - `Common` – 通用事件变换。  
  - `Action` – 专用于 `Action` 类事件的变换。  
  - `Row` – 专用于 `Row` 类事件的变换。  
- `EventApi.cs` – 快捷获取当前选中事件。  
- `EventSet.cs` – 批量操作多个事件快照。  
- `VarApi.cs` – 管理脚本会话中的变量。

### 3. Notebook
管理终端会话、代码执行与补全服务。

- `CommandHistory.cs` – 记录历史输入命令。  
- `NotebookCell.cs` – 单个代码单元格。  
- `NotebookCellResult.cs` – 单元格执行结果。  
- `NotebookKernel.cs` – 整合所有 notebook 组件。  
- `NotebookSession.cs` – 维护会话变量、单元格列表及当前关卡快照。  
- `RoslynCompletionSession.cs` – 调用 Roslyn 提供代码补全。

### 4. Scripting
配置脚本编译与执行环境，暴露可用的 API。

- `GameApi.cs` – 将 `GameLevelBridge` 的能力暴露给脚本。  
- `RoslynScriptHost.cs` – 调用 Roslyn 执行脚本代码。  
- `ScriptGlobals.cs` – 脚本可访问的全局对象（`session`、`vars`、`game` 等）。  
- `ScriptImports.cs` – 配置脚本编译所需的程序集引用与默认导入命名空间。默认导入包含 `RDETerminal.Domain.Transforms.Action` 与 `RDETerminal.Domain.Transforms.Row`。  
- **HotReload** – 支持在不重启游戏的情况下，动态编译并加载用户自定义脚本文件：
    - `UserScriptCatalog.cs` – 扫描 `Scripts` 文件夹下的 `.cs` 文件，缓存其内容与最后修改时间，提供快照与增量更新能力。  
    - `UserScriptCompiler.cs` – 使用 Roslyn 将用户脚本编译为内存中的程序集，返回编译结果（包含诊断信息、元数据引用）。  
    - `UserScriptReloadResult.cs` – 封装编译结果，包括成功状态、导出的命名空间/类型/成员，供终端补全系统集成。

### 5. UI
构建终端窗口并响应用户交互。

- `TerminalBootstrap.cs` – Harmony 补丁入口，完成依赖组装与窗口初始化。  
- `TerminalCompletionController.cs` – 管理补全面板的显示、选择与提交。  
- `TerminalInputEditing.cs` – 纯文本编辑规则：自动缩进、括号/引号配对、包裹选区、`Tab` 缩进、光标行检测等。不依赖 Unity 类型，便于独立推理与测试。行为参照 VS Code。  
- `TerminalTranscriptView.cs` – 管理代码执行结果的显示区域。  
- `TerminalUiBuilder.cs` – 动态创建并布局所有 UI 组件。  
- `TerminalWindow.cs` – 顶层 `MonoBehaviour`，负责窗口生命周期、输入循环与场景切换处理。除快捷键、历史、补全、单元格执行外，还负责：
  - 在 `Update` 中检测“本帧刚输入/删除/替换选区”，调用 `TerminalInputEditing` 应用编辑规则；
  - 处理 `Tab`/`Shift+Tab` 缩进（补全面板打开时 `Tab` 变为接受补全）；
  - 区分上下箭头是“切换历史”还是“移动光标”（依据光标是否在首行/末行）；
  - 恢复 `Escape` 导致的 InputField 取消编辑（Unity 会清空文本并失去焦点，需要手动还原）；
  - 在 `LateUpdate` 中记录上一帧的文本、光标、选区与焦点状态，供本帧规则判断使用。

## 实现细节

<details>

<summary>这是我个人的笔记，过了一下文件中比较复杂的代码，学习以前没用过的api</summary>

（可能有误）

### Rhythm Doctor

首先集中看一下游戏`Rhythm Doctor`相关的代码逻辑

编辑器场景对应的`MonoBehaviour`脚本类为`RDLevelEditor.scnEditor`：
- 这个类是单例模式，因此插件可以直接使用`scnEditor.instance`获取类实例
- `Editor.selectedControls`: 获取用户当前选中的事件（点击时间线空白处弹出的空事件也算“选中的事件”）
- `Editor.currentTab`: 获取当前所在的Tab
- `Editor.timeline`: 获取时间线类
- `Editor.CreateEventControl()`: 创建一个新的事件
- `Editor.SelectEventControl()`: 选中指定的事件
- `Editor.DeleteEventControl()`: 删除指定的事件
- 编辑器操作各个事件时使用的基类为`RDLevelEditor.LevelEventControl_Base`
    - 枚举`RDLevelEditor.LevelEventType`定义了所有的事件类型
    - `RDLevelEditor.LevelEvent_Base`是所有事件的基类
        - 所有事件的类型名称遵循命名规律`RDLevelEditor.LevelEvent_`+`LevelEventType`里定义的名称

`scnEditor`是一个极其庞大的类，这个类与其使用的衍生类囊括了所有编辑器相关的功能逻辑

### 项目代码

1. `ReflectionGameEventBridge.cs`

文件底部的`MemberMap`和`MemberAccessor`是一套通用的反射缓存与访问系统，管理某一个类型的所有可访问成员（属性和字段）。以`Capture`为入口的程序流如下：
- `gameEvent.GetType()`获取该事件的实际类型
- 调用`GetMap`获取该类型各属性的访问器`MemberMap`
    - 若之前建立过该类型的`MemberMap`，则直接返回Cache中存储的`MemberMap`；否则调用`BuildMap`创建一个新的`MemberMap`
- `BuildMap`用两个循环遍历目标类型的属性（Property）和字段（Field）
    - 索引器无法序列化，因此代码用`prop.GetIndexParameters().Length > 0`判断当前属性是不是索引器并跳过
- `MemberAccessor.CreateProperty`大量使用了表达式树（Expression Tree，一种将代码以数据结构的形式存储在内存中的技术，允许动态生成、分析代码）
    - `getter`的逻辑等价于：
    ```c#
    //Func<object, object>> ，接收object类型参数，返回object
    object Getter<T,V>(object obj) //obj对应objParam
    {
        //函数主体为boxExpr
        //转换输入参数，对应castObj
        T p = (T)obj;
        // 读取属性，对应propExpr
        V x = p.x;
        // 将返回值装箱为object，对应boxExpr
        return (object)x;
    }
    ```
    - 类似地，`setter`的逻辑等价于：
    ```c#
    //Action<object, object> ,接收两个object类型的参数，无返回值
    void Setter<T,V>(object obj, object val) //obj对应objParam，val对应valParam
    {
        //函数主体为call
        V v=(V)val;
        //Prop取决于传入的PropertyInfo
        (T)obj.Prop=v;
    }
    ```

利用表达式树访问未知类型的属性与字段要比单纯使用反射快得多。然而当初设计这套系统的chatgpt并不知道所有事件类型都有个基类，这堆复杂的操作相比直接调用还是慢了不少

2. `RoslynCompletionSession.cs`

roslyn相关的api可以在网上搜到，AI也能完成个95%；于是这里是一些AI没有一次做对的地方

`MefHostServices`为`AdhocWorkspace`提供了代码补全服务，创建时直接传入`MefHostServices.DefaultAssemblies`即可，无需手动传入`Microsoft.CodeAnalysis`相关的assembly。有很多链接，比如 https://stackoverflow.com/questions/42471015/roslyn-service-is-null ，反映`DefaultAssemblies`不足以获取`CompletionService`，但个人实测没有问题。可能是roslyn版本的问题

`ProjectInfo.Create`的`metadataReferences`参数提供roslyn分析代码时使用的外部程序集。比如往里面添加`a.dll`，roslyn便可以提供`a.dll`里的类型的补全。代码加入了`ScriptGlobals`所在的dll和`object`所在的dll，roslyn便能提供项目中定义的类型（变换函数与各种api）以及`.NET`基础类型的补全

`isSubmission: true`标记当前项目为“交互式会话”，允许代码像脚本一样被逐块执行，不需要写出完整的类和成员定义。不加这个参数会导致roslyn穷举所有可能的补全项，给出的内容与上下文毫无关系

`hostObjectType`指定代码可以访问的全局变量类

接下来是`GetItemsAsync`函数调用的`service.GetCompletionsAsync`。我以为这个函数可以直接模拟vscode的补全逻辑，结果单纯按照`SortText`排序`ItemsList`只能得到按照字母顺序排列的当前上下文可用的关键词。查阅`RoslynPad`项目的代码并与AI沟通后，我确认这是预期行为，IDE等调用服务的一方需要自行编写期望的排序逻辑。于是我“借用”了`RoslynPad`对排序的处理，并让gpt写了一个匹配前缀的函数

`GetCompletionsAsync`返回补全项时的行为不仅与代码上下文有关，还与传入的`CompletionTrigger`有关。可以从任何字符构建`CompletionTrigger`，但似乎只有部分特殊字符（见`CreateTrigger`函数，列出的字符可能不完全）能触发补全。`CompletionTrigger.Invoke`则可以强制触发补全

*题外话：在编写补全功能时，我和chatgpt掰扯了很久。因为我不知道补全功能该怎么做，由什么组件构成，我只能给出“请编写一个由Roslyn驱动的上下文补全功能，辅助用户在终端面板中编写代码”这样模糊的提示词。得到的结果自然是一团糟，UI全部糊成一团，也不知道roslyn是否正常运行。我尝试自行修复代码，但我对unity UI代码编写一窍不通，前前后后和chatgpt改了一个多星期却仍然在原地踏步。后面我突然“开窍”，意识到虽然chatgpt给的代码跑不了，但是这份代码指出了补全功能应有的全部组件；那么我手动拆分，叫chatgpt每次只实现一个组件，测试成功后再编写下一个组件会怎么样？效果竟出奇的好，目前的UI几乎是chatgpt一次就成功生成的，即使出现问题也是容易描述的细节问题*
</details>