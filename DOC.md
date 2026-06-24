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

### 2. 快速上手示例

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

### 3. 脚本 API 参考

#### 全局变量（`ScriptGlobals`）

| 变量 | 类型 | 说明 |
|------|------|------|
| `session` | `NotebookSession` | 会话管理器，包含已执行单元格、变量存储等。 |
| `vars` | `VarApi` | 变量操作：`Set(name, value)`、`TryGet(name, out value)`、`Get<T>(name)`。 |
| `events` | `EventApi` | 事件操作：`Sel()` 获取当前编辑器中选中的事件集。 |
| `sel` | `Func<EventSet>` | 等价于 `() => events.Sel()`，可简写为 `sel()`。 |
| `game` | `GameApi` | 关卡操作：`Capture()`、`Apply()`、`Current` 属性。 |
| `editor` | `EditorAdapter` | 编辑器底层操作：创建事件、获取选中事件等。 |
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

#### 查询函数（`Domain.Queries.EventQueries`）

| 函数 | 说明 |
|------|------|
| `TypeIs(evt, typeName)` | 判断事件类型是否匹配（不区分大小写）。 |
| `BarAtLeast(evt, bar)` | 事件所在小节 ≥ `bar`。 |
| `BeatAtMost(evt, beat)` | 事件所在拍 ≤ `beat`。 |

#### 变换函数（`Domain.Transforms`）

**通用变换（`Common.EventTransforms`）**

| 函数 | 说明 |
|------|------|
| `SetEventSpacingStartFrom(level, startBar, startBeat, spacing, filter)` | 对满足 `filter` 的事件重新设置时间位置，从 `(startBar, startBeat)` 开始，每个事件间隔 `spacing` 拍。 |

**文本专用变换（`Text.FloatingTextTransforms`）**

| 函数 | 说明 |
|------|------|
| `SplitAndAdvanceTextsWithOffset(level, filter, offset)` | 将满足条件的 `FloatingText` 文本按字符分割（插入 `/`），并在每个字符后生成 `AdvanceText` 事件，间隔 `offset` 拍。 |
| `RandomizeTextsAnglesPositions(level, minX, maxX, minY, maxY, filter, rng)` | 随机改变满足条件的 `FloatingText` 的显示角度和位置。 |
| `SetTextFontSize(level, newSize, filter)` | 修改满足条件的 `FloatingText` 的字体大小。 |
| `SetTextDuration(level, newDuration, filter)` | 修改满足条件的 `FloatingText` 的持续时长（淡出速率）。 |

> 所有变换函数均返回新的 `LevelDocument` 实例，原对象保持不变，便于链式调用。

### 4. 用户脚本热加载

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

## 项目架构

项目分为五个核心层：

### 1. Adapters
封装与游戏编辑器及事件系统的底层交互。

- `EditorAdapter.cs` – 包装编辑器内置方法，提供事件创建、选中事件获取等操作。  
- `GameLevelBridge.cs` – 实现关卡快照的捕获与应用（全量同步）。  
- `ReflectionGameEventBridge.cs` – 基于反射与表达式树，实现单个事件快照的捕获与应用（增量更新）。  
- `ReflectionUtil.cs` – 反射操作的通用辅助函数。

### 2. Domain
定义数据模型、核心类型以及脚本可调用的查询/变换函数。

- **Abstractions** – 底层接口定义。  
- **Core** – 核心数据类型：  
  - `ApplyResults.cs` – 应用快照后的执行结果统计。  
  - `EventFieldNames.cs` / `EventTypeNames.cs` – 事件字段名与类型名常量。  
  - `LevelDocument.cs` – 多个事件快照的容器，用于关卡级同步。  
  - `LevelEventSnapshot.cs` – 单个事件快照，携带变化追踪。  
  - `SnapshotValueCloner.cs` / `SnapshotValueComparer.cs` – 快照的深度克隆与相等比较。  
- **Queries** – 供脚本使用的过滤函数（如按类型、位置筛选）。  
- **Transforms** – 供脚本使用的变换函数：  
  - `Common` – 通用事件变换。  
  - `Text` – 专用于 `FloatingText` 事件的文本处理。  
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
- `ScriptImports.cs` – 配置脚本编译所需的程序集引用与默认导入命名空间。
- **HotReload** – 支持在不重启游戏的情况下，动态编译并加载用户自定义脚本文件：
    - `UserScriptCatalog.cs` – 扫描 `Scripts` 文件夹下的 `.cs` 文件，缓存其内容与最后修改时间，提供快照与增量更新能力。  
    - `UserScriptCompiler.cs` – 使用 Roslyn 将用户脚本编译为内存中的程序集，返回编译结果（包含诊断信息、元数据引用）。  
    - `UserScriptReloadResult.cs` – 封装编译结果，包括成功状态、导出的命名空间/类型/成员，供终端补全系统集成。

### 5. UI
构建终端窗口并响应用户交互。

- `TerminalBootstrap.cs` – Harmony 补丁入口，完成依赖组装与窗口初始化。  
- `TerminalCompletionController.cs` – 管理补全面板的显示、选择与提交。  
- `TerminalTranscriptView.cs` – 管理代码执行结果的显示区域。  
- `TerminalUiBuilder.cs` – 动态创建并布局所有 UI 组件。  
- `TerminalWindow.cs` – 顶层 `MonoBehaviour`，负责窗口生命周期、输入循环与场景切换处理。

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