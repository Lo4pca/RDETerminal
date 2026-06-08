# DOC

## 简介

`Rhythm Doctor Editor Terminal`是一个针对游戏"Rhythm Doctor"的BepInEx 5 Unity插件。该插件用Harmony插入自定义代码，在游戏内置的编辑器界面上方创建了一个执行代码的终端面板，允许用户调用项目内部创建的函数修改关卡事件。终端的代码执行与代码补全功能均由Roslyn支持

本项目的代码由AI生成，人工校验（然而我什么也没校验出来）。各AI参与的部分如下：
- ChatGPT：项目架构与具体实现
- Claude：代码重构
- DeepSeek：杂项问题解答

项目开发时参考了如下资料：
- BepInEx官方教程： https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/index.html
- Harmony官方教程： https://harmony.pardeike.net/articles/intro.html
- BepInEx 5插件案例： https://github.com/9thCore/RDEditorPlus
- Roslyn补全功能教程： https://www.strathweb.com/2018/12/using-roslyn-c-completion-service-programmatically
- Roslyn补全功能使用案例：
    - https://github.com/roslynpad/roslynpad/blob/main/src/RoslynPad.Editor.Windows/Shared/RoslynCodeEditorCompletionProvider.cs
    - https://github.com/dotnet/roslyn/issues/23447

`Random`提出了项目创意并负责实机测试，他在项目开发过程中给出了诸多建议

## 项目架构

项目由以下五个部分组成：
- Adapters: 负责插件与游戏逻辑的沟通
    - `EditorAdapter.cs`: 对游戏编辑器内置函数的包装
    - `GameLevelBridge.cs`: 负责对关卡截取快照或将快照应用到关卡
    - `ReflectionGameEventBridge.cs`: 负责对单个事件截取快照或将快照应用到事件
    - `ReflectionUtil.cs`: 反射操作的辅助函数
- Domain: 定义项目使用的数据类型与脚本可用的函数
    - Abstractions: 底层抽象接口
    - Core: 核心数据类型
        - `ApplyResults.cs`: 定义应用快照到事件或关卡后返回的应用结果
        - `EventFieldNames.cs`: 关卡事件的属性名
        - `EventTypeNames.cs`: 关卡事件的类型名
        - `LevelDocument.cs`: 用于包装多个事件快照，`GameLevelBridge`应用关卡时接收的数据类型
        - `LevelEventSnapshot.cs`: 关卡快照
        - `SnapshotValueCloner.cs`: 用于深度拷贝（deep-clone）关卡快照
        - `SnapshotValueComparer.cs`: 用于比较两个快照是否相同
    - Queries: 提供给脚本调用的用于过滤事件的函数
    - Transforms: 提供给脚本调用的用于修改事件的函数
        - Common: 适用于所有类型事件的函数
        - Text: 仅适用于`FloatingText`类型事件的函数
    - `EventApi.cs`: 获取当前选中事件的快捷入口
    - `EventSet.cs`: 用于批量处理多个事件快照
    - `VarApi.cs`: 处理当前脚本会话的变量
- Notebook: 执行代码的终端的组成部分
    - `CommandHistory.cs`: 记录历史执行的命令
    - `NotebookCell.cs`: 代码单元格
    - `NotebookCellResult.cs`: 代码执行结果
    - `NotebookKernel.cs`: 包含所有Notebook的核心组件
    - `NotebookSession.cs`: 管理当前会话的变量、代码单元格与当前正在处理的关卡
    - `RoslynCompletionSession.cs`: 负责调用Roslyn提供的补全功能
- Scripting: 执行脚本代码并定义可用函数范围
    - `GameApi.cs`: 暴露`GameLevelBridge`提供的api
    - `RoslynScriptHost.cs`: 负责调用Roslyn执行脚本代码
    - `ScriptGlobals.cs`: 脚本可使用的全局变量
    - `ScriptImports.cs`: 脚本可用的assembly与环境中已存在的引用
- UI: 渲染终端窗口并响应用户输入
    - `TerminalBootstrap.cs`: Harmony补丁的调用入口，负责组装终端的所有零件
    - `TerminalCompletionController.cs`: 代码补全功能相关的UI逻辑
    - `TerminalTranscriptView.cs`: 显示代码执行结果的UI逻辑
    - `TerminalUiBuilder.cs`: 创建并组装终端的UI组件
    - `TerminalWindow.cs`: 终端最顶层的`MonoBehaviour`脚本，负责所有组件的生命周期并响应用户输入

## 实现细节

这里会过一下文件中比较复杂的代码，学习以前没用过的api并分析架构中的各个文件是如何联系在一起的

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