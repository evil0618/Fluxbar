using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GlobalSidebar.Native
{
    /// <summary>
    /// 封装所有底层 Win32 API 的 P/Invoke 声明。
    /// 包含：窗口置顶、DWM 亚克力/云母材质、防穿透扩展样式、锁屏桌面挂载、系统事件钩子等。
    /// </summary>
    internal static class NativeMethods
    {
        #region ===== 窗口句柄与消息常量 =====

        /// <summary>WM_NCHITTEST 消息：用于自定义窗口命中测试，实现拖拽与缩放</summary>
        public const int WM_NCHITTEST = 0x0084;

        /// <summary>WM_DWMCOMPOSITIONCHANGED：DWM 合成状态变化通知</summary>
        public const int WM_DWMCOMPOSITIONCHANGED = 0x031E;

        /// <summary>WM_DESTROY：窗口销毁消息</summary>
        public const int WM_DESTROY = 0x0002;

        #endregion

        #region ===== SetWindowPos 置顶标志 =====

        /// <summary>
        /// SetWindowPos 的 uFlags 参数常量。
        /// HWND_TOPMOST 将窗口置于 Z 序顶部，即使窗口失去焦点也保持置顶。
        /// SWP_NOMOVE | SWP_NOSIZE 表示不改变窗口位置和大小，仅修改 Z 序。
        /// </summary>
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        /// <summary>
        /// 设置窗口的 Z 序、位置和尺寸。
        /// hWndInsertAfter 设为 HWND_TOPMOST 可实现全局置顶。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,          // 窗口句柄
            IntPtr hWndInsertAfter, // Z 序插入位置（HWND_TOPMOST 等）
            int X,                // 窗口左上角 X 坐标
            int Y,                // 窗口左上角 Y 坐标
            int cx,               // 窗口宽度
            int cy,               // 窗口高度
            uint uFlags           // 标志位组合
        );

        #endregion

        #region ===== 扩展窗口样式（防穿透） =====

        /// <summary>
        /// WS_EX_TRANSPARENT：使窗口对鼠标点击"透明"，点击事件穿透到下方窗口。
        /// 配合 WS_EX_LAYERED 使用，实现折叠态时透明区域的点击穿透。
        /// </summary>
        public const uint WS_EX_TRANSPARENT = 0x00000020;

        /// <summary>WS_EX_LAYERED：分层窗口，支持逐像素透明度</summary>
        public const uint WS_EX_LAYERED = 0x00080000;

        /// <summary>WS_EX_TOOLWINDOW：工具窗口，不在任务栏显示</summary>
        public const uint WS_EX_TOOLWINDOW = 0x00000080;

        /// <summary>WS_EX_TOPMOST：顶层窗口</summary>
        public const uint WS_EX_TOPMOST = 0x00000008;

        /// <summary>GWL_EXSTYLE：获取/设置扩展窗口样式的索引</summary>
        public const int GWL_EXSTYLE = -20;

        /// <summary>
        /// 获取窗口样式信息。
        /// nIndex=GWL_EXSTYLE 时返回扩展样式位掩码。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        /// <summary>
        /// 设置窗口样式信息。
        /// 用于动态添加/移除 WS_EX_TRANSPARENT 标志，控制点击穿透行为。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        #endregion

        #region ===== DWM 亚克力/云母材质 =====

        /// <summary>
        /// DWMWINDOWATTRIBUTE 枚举。
        /// DWMWA_USE_IMMERSIVE_DARK_MODE = 20：启用沉浸式暗色标题栏。
        /// DWMWA_SYSTEMBACKDROP_TYPE = 38：Windows 11 22H2+ 的系统背景材质类型。
        ///   值=1 → Auto（自动）
        ///   值=2 → Mica（云母）
        ///   值=3 → Acrylic（亚克力）
        ///   值=4 → Tabbed（标签页式云母）
        /// DWMWA_MICA_EFFECT = 1029：Windows 11 21H2 的云母效果（旧版兼容）。
        /// DWMWA_ACRYLIC_EFFECT = 1030：亚克力效果（旧版兼容，需配合 DWMWA_SYSTEMBACKDROP_TYPE）。
        /// </summary>
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        public const int DWMWA_MICA_EFFECT = 1029;
        public const int DWMWA_ACRYLIC_EFFECT = 1030;

        /// <summary>
        /// 系统背景材质类型枚举值（用于 DWMWA_SYSTEMBACKDROP_TYPE）。
        /// </summary>
        public enum DWM_SYSTEMBACKDROP_TYPE
        {
            DWMSBT_AUTO = 1,      // 自动选择
            DWMSBT_NONE = 2,      // 无效果
            DWMSBT_MAINWINDOW = 3, // Mica（云母）
            DWMSBT_TRANSIENTWINDOW = 4, // Acrylic（亚克力）
            DWMSBT_TABBEDWINDOW = 5    // Tabbed（标签页云母）
        }

        /// <summary>
        /// 设置 DWM 窗口属性，用于启用亚克力/云母等材质效果。
        /// </summary>
        /// <param name="hwnd">窗口句柄</param>
        /// <param name="dwAttribute">属性类型（DWMWA_SYSTEMBACKDROP_TYPE 等）</param>
        /// <param name="pvAttribute">属性值的指针</param>
        /// <param name="cbAttribute">属性值大小（字节）</param>
        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            ref int pvAttribute,
            int cbAttribute
        );

        #endregion

        #region ===== 锁屏桌面挂载（SetWinEventHook） =====

        /// <summary>
        /// 事件常量：EVENT_SYSTEM_DESKTOPSWITCH
        /// 当 Windows 切换桌面时触发（包括锁屏/解锁时从默认桌面切换到 Winlogon 桌面）。
        /// 通过监听此事件，我们可以在锁屏时将侧边栏重新挂载到 Winlogon 桌面，保持可见。
        /// </summary>
        public const uint EVENT_SYSTEM_DESKTOPSWITCH = 0x0020;

        /// <summary>事件范围：整个系统</summary>
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        /// <summary>
        /// 系统事件钩子回调委托。
        /// 当 SetWinEventHook 监听到指定事件时，系统通过此委托回调通知。
        /// </summary>
        public delegate void WinEventDelegate(
            IntPtr hWinEventHook,  // 钩子句柄
            uint eventType,        // 事件类型
            IntPtr hwnd,           // 事件关联窗口句柄
            int idObject,          // 对象 ID
            int idChild,           // 子对象 ID
            uint dwEventThread,    // 事件线程 ID
            uint dwmsEventTime     // 事件时间戳
        );

        /// <summary>
        /// 安装系统事件钩子。
        /// 用于监听 EVENT_SYSTEM_DESKTOPSWITCH，在锁屏时重新挂载窗口到 Winlogon 桌面。
        /// </summary>
        /// <param name="eventMin">监听的最小事件范围</param>
        /// <param name="eventMax">监听的最大事件范围</param>
        /// <param name="hmodWinEventProc">回调函数所在模块句柄（WINEVENT_OUTOFCONTEXT 时为 IntPtr.Zero）</param>
        /// <param name="lpfnWinEventProc">回调委托（必须保持引用防止 GC 回收）</param>
        /// <param name="idProcess">目标进程 ID（0=所有进程）</param>
        /// <param name="idThread">目标线程 ID（0=所有线程）</param>
        /// <param name="dwFlags">标志（WINEVENT_OUTOFCONTEXT=回调在调用线程中执行）</param>
        /// <returns>钩子句柄，UnhookWinEvent 时使用</returns>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags
        );

        /// <summary>
        /// 卸载系统事件钩子。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        #endregion

        #region ===== Winlogon 桌面挂载 =====

        /// <summary>最大允许的桌面名称长度</summary>
        private const int MAX_DESKTOP_NAME = 256;

        /// <summary>
        /// 打开指定名称的桌面。
        /// 用于在锁屏时打开 "Winlogon" 桌面，将侧边栏窗口挂载上去。
        /// </summary>
        /// <param name="lpszDesktop">桌面名称，如 "Winlogon"</param>
        /// <param name="dwFlags">访问标志（0）</param>
        /// <param name="fInherit">是否可继承（false）</param>
        /// <param name="dwDesiredAccess">访问权限（DESKTOP_READOBJECTS | DESKTOP_WRITEOBJECTS | DESKTOP_SWITCHDESKTOP）</param>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenDesktop(
            string lpszDesktop,
            int dwFlags,
            bool fInherit,
            uint dwDesiredAccess
        );

        /// <summary>
        /// 切换到指定桌面。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SwitchDesktop(IntPtr hDesktop);

        /// <summary>
        /// 关闭桌面句柄。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseDesktop(IntPtr hDesktop);

        /// <summary>
        /// 设置线程桌面。将当前线程绑定到指定桌面，使该线程创建的窗口显示在该桌面上。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetThreadDesktop(IntPtr hDesktop);

        /// <summary>DESKTOP_READOBJECTS 权限</summary>
        public const uint DESKTOP_READOBJECTS = 0x0001;
        /// <summary>DESKTOP_WRITEOBJECTS 权限</summary>
        public const uint DESKTOP_WRITEOBJECTS = 0x0080;
        /// <summary>DESKTOP_SWITCHDESKTOP 权限</summary>
        public const uint DESKTOP_SWITCHDESKTOP = 0x0100;

        #endregion

        #region ===== 辅助方法 =====

        /// <summary>
        /// 获取 WPF 窗口的 Win32 句柄（HWND）。
        /// </summary>
        public static IntPtr GetWindowHandle(Window window)
        {
            return new WindowInteropHelper(window).Handle;
        }

        /// <summary>
        /// 启用或禁用窗口的鼠标点击穿透。
        /// 折叠态：启用穿透（除小横条区域外，其余透明区域点击穿透到下方桌面）。
        /// 展开态：禁用穿透（整个面板可交互）。
        /// 
        /// 原理：通过 GetWindowLong 获取当前扩展样式，用位运算添加/移除 WS_EX_TRANSPARENT 标志，
        /// 再通过 SetWindowLong 写回。WS_EX_TRANSPARENT 使窗口对鼠标消息"透明"，
        /// 系统会将点击事件传递给 Z 序下方的窗口。
        /// </summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <param name="enable">true=启用穿透，false=禁用穿透</param>
        public static void SetClickThrough(IntPtr hWnd, bool enable)
        {
            // 获取当前扩展窗口样式
            int extendedStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            if (enable)
            {
                // 添加 WS_EX_TRANSPARENT 标志，使鼠标点击穿透
                SetWindowLong(hWnd, GWL_EXSTYLE, extendedStyle | (int)WS_EX_TRANSPARENT);
            }
            else
            {
                // 移除 WS_EX_TRANSPARENT 标志，恢复鼠标交互
                SetWindowLong(hWnd, GWL_EXSTYLE, extendedStyle & ~(int)WS_EX_TRANSPARENT);
            }
        }

        /// <summary>
        /// 将窗口设为全局置顶。
        /// 调用 SetWindowPos 传入 HWND_TOPMOST，使窗口始终位于 Z 序顶部。
        /// SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE 表示不改变位置、大小，不激活窗口。
        /// </summary>
        public static void SetTopmost(IntPtr hWnd)
        {
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>
        /// 尝试为窗口启用 Windows 11 原生亚克力（Acrylic）背景材质。
        /// 优先使用 DWMWA_SYSTEMBACKDROP_TYPE（Win11 22H2+），
        /// 若失败则回退到 DWMWA_ACRYLIC_EFFECT（旧版兼容）。
        /// </summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <returns>true 表示成功启用</returns>
        public static bool EnableAcrylic(IntPtr hWnd)
        {
            // 先尝试 Win11 22H2+ 的新 API
            int backdropType = (int)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW; // Acrylic
            int result = DwmSetWindowAttribute(
                hWnd,
                DWMWA_SYSTEMBACKDROP_TYPE,
                ref backdropType,
                sizeof(int)
            );

            if (result == 0) return true; // S_OK

            // 回退到旧版 API
            int acrylicValue = 1;
            result = DwmSetWindowAttribute(
                hWnd,
                DWMWA_ACRYLIC_EFFECT,
                ref acrylicValue,
                sizeof(int)
            );

            return result == 0;
        }

        /// <summary>
        /// 尝试为窗口启用 Windows 11 云母（Mica）背景材质。
        /// </summary>
        public static bool EnableMica(IntPtr hWnd)
        {
            int backdropType = (int)DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW; // Mica
            int result = DwmSetWindowAttribute(
                hWnd,
                DWMWA_SYSTEMBACKDROP_TYPE,
                ref backdropType,
                sizeof(int)
            );

            if (result == 0) return true;

            // 回退到旧版 API
            int micaValue = 1;
            result = DwmSetWindowAttribute(
                hWnd,
                DWMWA_MICA_EFFECT,
                ref micaValue,
                sizeof(int)
            );

            return result == 0;
        }

        /// <summary>
        /// 启用沉浸式暗色模式，使标题栏区域与暗色主题融合。
        /// </summary>
        public static void EnableDarkMode(IntPtr hWnd)
        {
            int value = 1;
            DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }

        #endregion
    }
}
