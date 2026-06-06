# Visual Studio Dock Indicator 的显示规律

Visual Studio 的 docking indicator 可以粗略理解为两类目标：

1. 当前目标区域的停靠提示
2. 整个 IDE 主窗口边缘的停靠提示

所以有时会看到 5 格，有时会看到 9 格。

## 5 格提示什么时候出现

当拖动一个窗口，并悬停到某个具体可停靠区域上时，Visual Studio 通常会显示一组针对当前区域的 docking guide。

这组提示可以理解为：

text       Top Left  Tab/Fill  Right       Bottom 

含义如下：

| 位置 | 含义 |
|---|---|
| Top | 停靠到当前目标区域上方 |
| Bottom | 停靠到当前目标区域下方 |
| Left | 停靠到当前目标区域左侧 |
| Right | 停靠到当前目标区域右侧 |
| Center | 并入当前目标区域，成为同一组 tab |

这里最容易误解的是中间那一格。它不是“显示在中间”，而是表示把正在拖动的窗口加入当前目标区域，成为同一个 tab group。

比如：

- 拖到 Solution Explorer 上，中间格表示和 Solution Explorer 成为同一组 tool window tab。
- 拖到 document well 上，中间格表示成为文档 tab。

所以 5 格提示本质上是：

> 相对于当前鼠标指向的 pane/group，应该如何停靠。

## 9 格提示什么时候出现

9 格通常不是一个独立的 3x3 布局规则，而是两套提示同时出现：

1. 主 IDE 边缘的全局停靠目标
2. 当前 pane/group 的局部停靠目标

可以把它理解成这样：

text                     IDE Top                      Pane Top IDE Left      Pane Left   Tab/Fill   Pane Right      IDE Right                     Pane Bottom                      IDE Bottom 

也可以拆开看：

text 全局 IDE 边缘目标：                      IDE Top  IDE Left                                      IDE Right                      IDE Bottom 

text 当前 pane/group 局部目标：                      Pane Top         Pane Left   Tab/Fill   Pane Right                     Pane Bottom 

合起来就是：

text 4 个 IDE 边缘目标 + 5 个当前 pane 目标 = 9 个提示 

也就是说，当你拖动窗口时，如果鼠标既处于 Visual Studio 主窗口范围内，又悬停在某个具体可停靠区域上，VS 可能会同时显示：

- 一套全局目标：停靠到整个 IDE 的左、右、上、下边。
- 一套局部目标：停靠到当前 pane 的左、右、上、下，或者加入当前 pane 的 tab group。

因此：

> 5 格表示当前区域的局部 docking guide。  
> 9 格表示全局 IDE 边缘 guide 和当前区域 guide 同时出现。

## Tool window 和 document window 的区别

Visual Studio 对 tool window 和 document window 的 docking 规则不同。

## Tool window

Tool window 指的是 Solution Explorer、Properties、Output、Error List 这类窗口。

它们的停靠规则比较自由，通常可以：

- 停靠到 IDE 主窗口边缘。
- 停靠到另一个 tool window 的上、下、左、右。
- 和另一个 tool window 合并为同一个 tab group。
- 浮动。
- Auto-hide。
- 在部分情况下参与更复杂的窗口布局。

所以拖动 tool window 时，更容易看到完整的 docking indicator，包括 5 格或 9 格。

## Document window

Document window 指的是代码文件、designer、普通 editor tab 这类窗口。

它们主要属于 document well，也就是编辑区。

Document window 的停靠范围通常更受限制，主要可以：

- 在 document well 内成为 tab。
- 在 document well 内创建水平或垂直 tab group。
- 浮动成独立 document window。

但 document window 通常不能像 tool window 一样，随意停靠到 IDE 左边或右边，变成 Solution Explorer 那种工具窗口区域。

所以拖动 document window 时，看到的 docking guide 通常更偏向 document well 内部的布局提示，而不是完整的 tool-window 式停靠提示。

## 一个简单判断方法

可以按下面这个规律判断：

text 拖 tool window 到空白 IDE 区域附近： 通常会看到主窗口边缘停靠目标。  拖 tool window 到另一个 tool window 上： 通常会看到 5 格局部 guide。 如果同时显示主窗口边缘目标，就会形成 9 格提示。  拖 document window 到 document well： 通常显示 document guide，用于成为 tab、创建水平 tab group 或垂直 tab group。  拖 document window 到 tool window 区域： 通常不会给出完整的 tool-window 式 docking 选择，因为 document window 不能自由进入 tool window layout。 

## 总结

Visual Studio 的 docking indicator 可以这样记：

text 5 格 = 当前 pane/group 的局部停靠提示 9 格 = IDE 主窗口边缘提示 + 当前 pane/group 的局部停靠提示 

中间格的含义通常是加入当前目标区域成为 tab，而不是停靠到屏幕中间。

Tool window 的停靠自由度高，所以更容易触发完整的 docking indicator。Document window 主要受限在 document well 内，因此它的 docking 行为更偏向编辑区内部的 tab 和 split 布局。