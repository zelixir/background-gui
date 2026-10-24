# vdesk —— 隐藏桌面 GUI 自动化 CLI

在 Win32 **隐藏桌面**（非输入桌面）里运行 GUI 程序，并从命令行对其进行**消息级自动化**与截图。程序在用户不可见的桌面上完整运行，脚本全程不切换桌面、不注入任何鼠标/键盘输入，用户侧零干扰。

架构要点：自动化代码（本工具）始终留在**默认桌面**，通过「窗口句柄是会话级全局对象」跨桌面操作目标窗口——枚举（EnumDesktopWindows）、消息（SendMessageTimeoutW/PostMessageW）、渲染截图（PrintWindow）。**绝不**把自动化代码放进隐藏桌面执行（本机非输入桌面上 Python _ctypes/_imaging 等 DLL 初始化会失败），**绝不**调用 SendInput / keybd_event / mouse_event / SetCursorPos / SwitchDesktop。

- 工具名：`vdesk`
- 目标框架：net8.0（框架依赖，需安装 Microsoft.NETCore.App 8.x 运行时）
- 外部依赖：**零**（全部 P/Invoke + BCL，PNG 编码器为手写实现）
- 输出约定：所有命令输出 UTF-8 JSON 到 stdout；运行错误输出 `{"error":"..."}` 到 stderr 且退出码 **3**；用法错误退出码 **2**；成功退出码 0
- hwnd 表示：JSON 中一律为**十进制数字字符串**（也接受 `0x` 前缀十六进制输入）

## 构建

```powershell
dotnet build src/vdesk -c Release
```

## 发布（单文件，框架依赖）

```powershell
dotnet publish src/vdesk -c Release -r win-x64 --no-self-contained `
  -p:PublishSingleFile=true -p:PublishDir=D:Codeackground-guipublish```

发布产物：**`D:Codeackground-guipublishdesk.exe`**（约 192 KB 单文件，依赖系统已装的 .NET 8 桌面运行时即可运行）。

## 命令一览

| 命令 | 作用 |
|---|---|
| `desktop list` | 列出当前窗口站全部桌面 |
| `desktop create <name>` | 创建隐藏桌面（已存在则 `created:false`） |
| `desktop destroy <name> [--kill]` | 销毁桌面；`--kill` 先终止其上全部进程。**拒绝操作 Default** |
| `launch` | 在指定桌面启动 exe 并等待其窗口出现 |
| `windows` | 枚举桌面顶层窗口 |
| `controls --hwnd` | 平铺列出全部后代控件 |
| `text --hwnd` | 读窗口文本（WM_GETTEXT） |
| `menu --hwnd` | 读菜单树（递归 3 层，popup 的 id 为 -1） |
| `invoke --hwnd` | 发菜单命令（按路径匹配）或直接发命令 ID |
| `select --hwnd` | ListBox/ComboBox/TreeView/ListView 选中项（自动补通知） |
| `items --hwnd` | 列出列表类控件全部条目 |
| `shot --hwnd` | PrintWindow 截图为 PNG + 全黑检测 |
| `close --hwnd` | PostMessage WM_CLOSE |
| `kill --pid` | 先优雅等待，超时 TerminateProcess |

## 使用示例（含真实输出）

以下输出全部来自本仓库自测（Windows 11 26200，net8.0）。

### desktop

```text
> vdesk desktop list
{"desktops":["Default"]}

> vdesk desktop create vdesk_test
{"name":"vdesk_test","created":true,"note":"an empty desktop with no attached process/handle may be reclaimed by the system; launch re-creates it on demand"}

> vdesk desktop destroy vdesk_test --kill      # 桌面上有进程时
{"name":"vdesk_test","destroyed":true,"windows":15,"processes":1,"killed":1}

> vdesk desktop destroy Default                # 红线保护
(stderr) {"error":"refusing to destroy the default desktop"}   # 退出码 3
```

### launch —— 在隐藏桌面启动 notepad 并等待窗口

```text
> vdesk launch --desktop vdesk_test --exe C:WindowsSystem32
otepad.exe --wait-class Notepad --timeout 30
{"pid":18480,"hwnd":"272352","class":"Notepad","title":"无标题 - Notepad","launcher_pid":18584,"note":"pid 18584 exposed no window (launcher/exec-alias); matched by class/title filter, real window pid is 18480","desktop_created":true}
```

说明：Win11 商店版 notepad 的 System32
otepad.exe 是毫秒级退出的启动 stub，真实窗口属于包进程。**给了 `--wait-class`/`--wait-title` 时**，launch 会匹配桌面上第一个无主且满足过滤器的窗口并返回其真实 pid（附 `note` 与 `launcher_pid`）；两个过滤器都不给时严格按「启动 pid + 无主」匹配。目标桌面不存在时 launch 会自动创建（`desktop_created:true`）。

### windows / controls / text

```text
> vdesk windows --desktop vdesk_test
{"desktop":"vdesk_test","windows":[{"hwnd":"272352","class":"Notepad","title":"无标题 - Notepad","pid":18480,"visible":true,"rect":[833,462,2880,1538]}]}

> vdesk text --hwnd 272352
{"hwnd":"272352","text":"无标题 - Notepad"}

> vdesk controls --hwnd <main>     # 一层平铺全部后代控件
{"hwnd":"...","controls":[{"hwnd":"...","class":"WindowsForms10.LISTBOX.app...","text":"","id":...,"visible":true,"rect":[x,y,w,h]}, ...]}
```

### menu / invoke —— 菜单树与命令触发

```text
> vdesk menu --hwnd <main>
{"hwnd":"...","menus":[{"index":0,"text":"&Tools","id":-1,"sub":[{"index":0,"text":"&Options","id":257,"sub":[]}]},{"index":1,"text":"&Help","id":-1,"sub":[{"index":0,"text":"About &VDeskTest","id":259,"sub":[]}]}]}

> vdesk invoke --hwnd <main> --menu "Tools>Options"
{"cmdid":257,"path":"Tools>Options","hwnd":"..."}

> vdesk invoke --hwnd <main> --cmdid 259
{"cmdid":259,"hwnd":"..."}
```

`--menu` 匹配规则：按 `>`（全角 `＞` 亦可）分段；每段先精确匹配、再双向子串匹配（均忽略大小写）；比较前去掉 `&` 与制表符快捷键后缀、去首尾空格。中文与英文菜单都能匹配。找不到时输出已匹配路径与该级全部候选帮助排错：

```text
> vdesk invoke --hwnd <main> --menu "Tools>Nope"
(stderr) {"error":"menu segment "Nope" not found at level 1","path":"Tools>Nope","level":1,"segment":"Nope","matched":["Tools"],"candidates":["Options"]}   # 退出码 3
```

### select —— 四类列表控件选中（自动补通知）

```text
> vdesk select --hwnd <listbox> --index 2
{"control":"ListBox","class":"WindowsForms10.LISTBOX.app...","index":2,"notified":true,"text":"Fruit 3"}

> vdesk select --hwnd <combobox> --text Blue
{"control":"ComboBox","class":"WindowsForms10.COMBOBOX.app...","index":2,"notified":true,"text":"Blue"}

> vdesk select --hwnd <treeview> --text "Beta One"
{"control":"SysTreeView32","class":"...SysTreeView32...","index":4,"notified":true,"text":"Beta One"}

> vdesk select --hwnd <listview> --index 3
{"control":"SysListView32","class":"...SysListView32...","index":3,"notified":true,"text":"Item Four"}

> vdesk select --hwnd <listbox> --index 99
(stderr) {"error":"--index 99 out of range (count=6)"}   # 退出码 3
```

行为细节：
- **ListBox**：`LB_SETCURSEL` + 向父窗口补发 `WM_COMMAND(LBN_SELCHANGE)`（控件本身不发通知，必须补）
- **ComboBox**：`CB_SETCURSEL` + `WM_COMMAND(CBN_SELCHANGE)` 同上
- **TreeView**：TVM_GETNEXTITEM 从根 DFS 枚举（`--index` 为深度优先顺序），`--text` 用跨进程 TVM_GETITEMW（VirtualAllocEx/Write/ReadProcessMemory）读条目文字；选中用 TVM_SELECTITEM(TVGN_CARET)，控件自动发 TVN_SELCHANGED，无需补通知
- **ListView**：先 LVM_SETITEMSTATE(wParam=-1) 清全部选中，再对目标项设置 LVIS_SELECTED|LVIS_FOCUSED（跨进程 LVITEM）；控件自动发 LVN_ITEMCHANGED
- owner-draw 列表条目取不到文字：`--index` 仍可选中，输出附 `"text_available":false`；`--text` 无法匹配时报错提示改用 `--index`

### items —— 列出全部条目

```text
> vdesk items --hwnd <listbox>
{"control":"ListBox","class":"...","count":6,"items":[{"index":0,"text":"Fruit 1"},...]}

> vdesk items --hwnd <owner-draw-list>      # 例如 Total Commander 配置对话框左栏
{"control":"ListBox","class":"LCLListBox","count":24,"items":[],"note":"owner-draw control: no strings via messages"}
```

### shot —— PrintWindow 截图

```text
> vdesk shot --hwnd 272352 --out notepad.png
{"w":2880,"h":1538,"blank":false,"out":"D:Code	mpdesk-selftestshot_notepad.png"}
```

优先 `PrintWindow(flag=2 PW_RENDERFULLCONTENT)`，失败回退 flag=0；GetDIBits 取 32bpp top-down 像素，PNG 由内置手写编码器生成（zlib 流 = 0x78 0x9C + DeflateStream + adler32 大端，CRC32 表驱动，IHDR 8bit RGB），不依赖 System.Drawing。写完抽样像素判断全黑，`blank:true` 时附 `warning`（退出码仍为 0）。

### close / kill

```text
> vdesk close --hwnd 272352
{"closed":true,"hwnd":"272352"}

> vdesk kill --pid 55276 --timeout 5
{"killed":true,"graceful":false,"pid":55276}
```

`kill` 先 WaitForSingleObject 等自然退出（默认 12s，`--timeout` 可调），超时才 TerminateProcess；`graceful:false` 表示走了强杀。

## 已知限制

1. **owner-draw 列表取不到文字**：owner-draw 控件（如 Total Commander 10.5 配置对话框的 LCLListBox）经消息取不到条目文字（LB_GETTEXTLEN 返回 0），`items` 返回空列表附 note；`select --index` 按索引选中不受影响。
2. **提权程序无法直接 launch**：vdesk 以当前（非提升）令牌运行，CreateProcessW 不能直接启动需要提权的程序；对提权进程做 kill / 跨进程内存读取也会因权限不足失败。需要时请在提升环境运行 vdesk。
3. **坐标点击不可用**：不实现任何鼠标/键盘注入（SendInput/mouse_event/SetCursorPos 全部禁用），一切交互靠控件消息完成；非输入桌面上坐标注入本就不可靠。
4. **空桌面会被系统即时回收**：CreateDesktopW 的桌面对象在「无句柄 + 无附着进程」时立刻销毁，因此 `desktop create` 与后续命令之间桌面可能已不存在（`created:false` 仅在桌面当前确实存在时出现）。`launch` 已内置自动重建；`desktop destroy` 对已回收桌面会按规范报「桌面不存在」（退出码 3）。
5. **菜单路径匹配按文本**：菜单文字本地化变化会导致 `--menu` 匹配失败（有 candidates 输出可排错）；跨语言（中文菜单配英文路径）无法匹配。
6. **消息超时 3s**：所有 SendMessageTimeoutW 带 SMTO_ABORTIFHUNG、默认 3000ms；对未响应窗口不会挂死，但取该窗口信息会得到空值。

## 实现备忘（踩坑实录）

- `SendMessageTimeoutW` 的结果在 `lpdwResult` **指针参数**里而非返回值；P/Invoke 签名 `out UIntPtr`，WPARAM/LPARAM 用 UIntPtr/IntPtr 防 64 位截断。
- **跨桌面窗口句柄只在「调用线程持有其所属桌面的打开句柄期间」可用**：IsWindow/GetWindowThreadProcessId/SendMessage 在句柄有效但桌面句柄已关时全部失败（实测 ERROR_INVALID_WINDOW_HANDLE / tid=0）。因此每个按 hwnd 操作的命令都会先定位并打开窗口所属桌面（`Href`），命令期间持有。
- **LB_GETTEXT / CB_GETLBTEXT 跨桌面时由系统封送**：lParam 缓冲按**发送进程**地址解释，必须传本地缓冲（与 WM_GETTEXT 同理）；远程缓冲（VirtualAllocEx）方案对这两条消息无效。而 TVM_GETITEMW / LVM_GETITEMTEXTW 不在封送名单，必须走经典远程缓冲（TVITEMW/LVITEMW + VirtualAllocEx/WriteProcessMemory/ReadProcessMemory）。两类消息行为相反，均已实测验证。
- 常数勘误（以 SDK winuser.h/commctrl.h 为准）：`LB_GETTEXTLEN=0x018A`（0x018C 是 LB_SELECTSTRING）、`CB_SETCURSEL=0x014E`（0x0143 是 CB_ADDSTRING）、`LVM_SETITEMSTATE=0x102B`（LVM_FIRST+43）。
- 枚举回调（EnumDesktopsW/EnumDesktopWindows/EnumChildWindows）的委托在枚举期间以局部变量保活；提前终止枚举（回调返回 false）会让枚举 API 返回 FALSE，**不能**把它当错误信号。
- OpenDesktopW 权限掩码用 0xFF（READOBJECTS|CREATEWINDOW|CREATEMENU|HOOKCONTROL|JOURNALRECORD|JOURNALPLAYBACK|ENUMERATE|WRITEOBJECTS），不含 SWITCHDESKTOP；CreateDesktopW 用 GENERIC_ALL(0x10000000)。
- 进程句柄不跨命令保留：launch 后立即关闭 hProcess/hThread，后续命令靠 pid 重新 OpenProcess。
- 窗口标题可能含孤立代理字符，JSON 序列化时替换为 U+FFFD，避免输出流损坏。

## 仓库结构

```text
background-gui/
├── README.md
├── .gitignore
├── publish/               # 发布输出（gitignore）
└── src/vdesk/
    ├── vdesk.csproj       # net8.0，零外部依赖
    ├── Program.cs         # 入口：UTF-8/DPI 初始化、错误分流（exit 2/3）
    ├── Win32.cs           # 全部 P/Invoke 声明与常量
    ├── W.cs               # 辅助层：Args 解析、Href（定位并持有窗口所属桌面）、错误类型
    ├── Json.cs            # 极简 JSON 序列化（含孤立代理字符处理）
    ├── XMem.cs            # 跨进程内存（OpenProcess 0x38 + VirtualAllocEx/Read/Write/FreeEx）
    ├── Png.cs             # 手写 PNG 编码器（zlib/CRC32/adler32）+ 全黑检测
    ├── Cmds.cs            # 分发 + desktop/launch/close/kill
    ├── CmdsInspect.cs     # windows/controls/text/menu
    ├── CmdsInteract.cs    # invoke/select/items
    └── CmdsShot.cs        # shot
```
