# 实战示例：隐藏桌面遍历 Total Commander 配置对话框

2026-10-24 真实验收记录。目标：在用户完全无感的前提下，把 TC 10.5「配置→选项」对话框的 24 个分类各截图一张。
全程仅使用 vdesk 命令（约 35 次进程调用），总耗时 18.8s，用户侧零输入注入、零桌面切换。

```text
前置：agent 专用 TC 配置 wincmd_agent.ini（onlyonce=0, TrayIcon=0, 独立于用户配置）
```

## 命令序列（省略重复）

```text
:: 1. 在隐藏桌面拉起 TC，等待主窗口
vdesk launch --desktop AgentDesk_CLI --exe "C:\Program Files\totalcmd\TOTALCMD64.EXE" ^
  --args "/i=D:\tmp\vdesk-acceptance\wincmd_agent.ini /F=D:\tmp\vdesk-acceptance\wcx_ftp_agent.ini" ^
  --cwd "C:\Program Files\totalcmd" --wait-class TTOTAL_CMD --timeout 30
→ {"pid":62196,"hwnd":"1385158","class":"TTOTAL_CMD","title":"Total Commander (x64) 10.50b8 - NOT REGISTERED"}

:: 2. 读菜单树定位 [配置→选项]
vdesk menu --hwnd 1385158
→ {"menus":[...,{"index":5,"text":"配置(&O)","id":-1,"sub":[{"index":0,"text":"选项(&O)...","id":233,...}]},...]}

:: 3. 按菜单路径打开配置对话框（中文路径文字匹配）
vdesk invoke --hwnd 1385158 --menu "配置>选项"
→ {"cmdid":233,"path":"配置>选项","hwnd":"1385158"}

:: 4. 定位对话框与分类列表
vdesk windows --desktop AgentDesk_CLI --class TDLGCONFIGALL   → dlg hwnd=1647396
vdesk controls --hwnd 1647396                                  → 左栏 LCLListBox hwnd=329354

:: 5. 直接读到全部分类名（LB 文字走系统封送，本地缓冲即可）
vdesk items --hwnd 329354
→ {"control":"ListBox","class":"LCLListBox","count":24,"items":[{"index":0,"text":"布局"},...,{"index":23,"text":"其他"}]}

:: 6. 逐项选中 + 截图（切页必须补 WM_COMMAND(LBN_SELCHANGE)，vdesk select 已内置）
vdesk select --hwnd 329354 --index 0    → {"control":"ListBox","index":0,"notified":true}
vdesk shot   --hwnd 1647396 --out 01.png → {"w":965,"h":765,"blank":false}
（…循环 24 次…）
结果：24/24 张全部有效，MD5 两两互异（每页真实不同）

:: 7. 收尾（对话框 WM_CLOSE=取消，不落任何设置）
vdesk close --hwnd 1647396
vdesk close --hwnd 1385158              → TC 退出 → 隐藏桌面自动销毁
vdesk desktop destroy AgentDesk_CLI     → {"error":"desktop does not exist (Win32 error 2)"}（已自动销毁，属预期）
```

## 本例证实的设计要点

1. **自动化代码不进隐藏桌面**：vdesk 进程始终在默认桌面，全部操作走句柄（EnumDesktopWindows / SendMessage / PrintWindow 均可跨桌面）。
2. **菜单别背命令号**：cm_Config=490 直接发无效；必须 GetMenu 读树按文字拿真实 ID。
3. **LB/CB 文字系统封送**（本地缓冲直读），TVM/LVM 条目才需要 VirtualAllocEx 远程缓冲。
4. **设置类选择必须补通知**：LB_SETCURSEL/CB_SETCURSEL 本身不触发 LBN/CBN_SELCHANGE，vdesk 已内置补发。
5. **截图永远 PrintWindow(PW_RENDERFULLCONTENT)**，非前台/非输入桌面照常渲染。
6. 消息一律按 PID/HWND 精确制导——同机运行的用户自己的 TC 实例全程无扰。
