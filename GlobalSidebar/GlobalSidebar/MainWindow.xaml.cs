using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using GlobalSidebar.Native;
using GlobalSidebar.ViewModels;

namespace GlobalSidebar
{
    /// <summary>
    /// MainWindow 的交互逻辑。
    /// 
    /// 核心职责：
    /// 1. 重写 WndProc 拦截 WM_NCHITTEST，实现自定义标题栏拖拽
    /// 2. 管理折叠/展开动画的触发与完成回调
    /// 3. 通过 WS_EX_TRANSPARENT 控制防穿透行为
    /// 4. 监听 Deactivated 事件实现失焦自动收回
    /// 5. 通过 SetWinEventHook 监听桌面切换，实现锁屏显示
    /// </summary>
    public partial class MainWindow : Window
    {
        #region ===== 私有字段 =====

        /// <summary>ViewModel 实例引用</summary>
        private readonly MainViewModel _viewModel;

        /// <summary>窗口句柄（HWND），在 SourceInitialized 后获取</summary>
        private IntPtr _hwnd;

        /// <summary>HwndSource，用于挂钩 WndProc</summary>
        private HwndSource? _hwndSource;

        /// <summary>
        /// 系统事件钩子句柄。
        /// 用于监听 EVENT_SYSTEM_DESKTOPSWITCH，在锁屏时重新挂载窗口。
        /// 必须作为类字段保持引用，防止委托被 GC 回收导致钩子失效。
        /// </summary>
        private IntPtr _winEventHook;

        /// <summary>
        /// 系统事件回调委托引用。
        /// 必须保持为类字段，防止 GC 回收导致回调失效（这是 SetWinEventHook 的常见陷阱）。
        /// </summary>
        private NativeMethods.WinEventDelegate? _winEventProc;

        /// <summary>是否正在执行动画，防止动画期间重复触发</summary>
        private bool _isAnimating;

        #endregion

        #region ===== 构造函数 =====

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = (MainViewModel)DataContext;
            _viewModel.CurrentState = SidebarState.Collapsed;

            // 订阅窗口事件
            Loaded += OnLoaded;
            SourceInitialized += OnSourceInitialized;
            Deactivated += OnDeactivated;
        }

        #endregion

        #region ===== 窗口初始化 =====

        /// <summary>
        /// 窗口加载完成时执行。
        /// 设置窗口位置到屏幕左侧边缘，启用 DWM 材质，设置置顶。
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _hwnd = NativeMethods.GetWindowHandle(this);

            // 窗口定位到屏幕左侧边缘，Y=0，宽度=340（12折叠条+320面板+8边距）
            Left = 0;
            Top = 0;

            // 启用 DWM 暗色模式
            NativeMethods.EnableDarkMode(_hwnd);

            // 尝试启用亚克力背景材质
            NativeMethods.EnableAcrylic(_hwnd);

            // 全局置顶
            NativeMethods.SetTopmost(_hwnd);

            // 初始状态为折叠态，启用防穿透（透明区域点击穿透到下方桌面）
            UpdateClickThrough(SidebarState.Collapsed);

            // 安装系统桌面切换事件钩子（锁屏检测）
            InstallWinEventHook();
        }

        /// <summary>
        /// 窗口源初始化完成，挂钩 WndProc。
        /// WndProc 是 Win32 消息循环的核心，通过重写它我们可以拦截底层消息。
        /// </summary>
        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = NativeMethods.GetWindowHandle(this);
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            _hwndSource?.AddHook(WndProc);
        }

        #endregion

        #region ===== WndProc 重写（核心） =====

        /// <summary>
        /// 重写 WndProc（窗口过程函数），拦截底层 Win32 消息。
        /// 
        /// WM_NCHITTEST 消息处理：
        /// 当系统或应用程序需要确定鼠标光标位于窗口的哪个区域时，
        /// 会发送 WM_NCHITTEST 消息。返回值指示命中区域类型：
        ///   HTCLIENT (1) = 客户区，由 WPF 正常处理
        ///   HTCAPTION (2) = 标题栏，系统会启用拖拽行为
        ///   HTTRANSPARENT (-1) = 透明，消息传递给 Z 序下方的窗口
        /// 
        /// 通过在标题栏区域返回 HTCAPTION，我们实现了自定义标题栏的拖拽移动，
        /// 无需手动处理 MouseLeftButtonDown + MouseMove 的拖拽逻辑，
        /// 系统原生拖拽更流畅、更符合 Windows 交互规范。
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case NativeMethods.WM_NCHITTEST:
                    return HandleNcHitTest(hwnd, msg, wParam, lParam, ref handled);

                case NativeMethods.WM_DESTROY:
                    // 窗口销毁时卸载事件钩子
                    UninstallWinEventHook();
                    break;
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// 处理 WM_NCHITTEST 消息，实现自定义命中测试。
        /// 
        /// 逻辑：
        /// 1. 获取鼠标在屏幕上的坐标（lParam 的低字=X，高字=Y）
        /// 2. 将屏幕坐标转换为窗口内坐标
        /// 3. 判断鼠标是否在标题栏区域 → 返回 HTCAPTION（可拖拽）
        /// 4. 判断鼠标是否在折叠态小横条区域 → 返回 HTCLIENT（可点击）
        /// 5. 其他透明区域 → 返回 HTTRANSPARENT（穿透到下方窗口）
        /// </summary>
        private IntPtr HandleNcHitTest(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 从 lParam 提取鼠标屏幕坐标
            // lParam 低 16 位 = X 坐标，高 16 位 = Y 坐标
            int screenX = (int)(short)(lParam.ToInt64() & 0xFFFF);
            int screenY = (int)(short)((lParam.ToInt64() >> 16) & 0xFFFF);

            // 将屏幕坐标转换为 WPF 窗口的逻辑坐标
            var screenPoint = new Point(screenX, screenY);
            Point windowPoint = PointFromScreen(screenPoint);

            // 展开态：判断是否在标题栏区域
            if (_viewModel.CurrentState == SidebarState.Expanded)
            {
                // 标题栏区域：ExpandedPanel 内的 TitleBar
                // ExpandedPanel 的 Canvas.Left=12，标题栏高度=40
                if (windowPoint.X >= 12 && windowPoint.X <= 332 && windowPoint.Y <= 40)
                {
                    // 排除关闭按钮区域（右上角 28x28），让按钮能正常接收点击
                    if (windowPoint.X >= 304 && windowPoint.Y <= 28)
                    {
                        // 关闭按钮区域，返回 HTCLIENT 让 WPF 正常处理点击
                        handled = true;
                        return (IntPtr)1; // HTCLIENT
                    }

                    // 标题栏区域，返回 HTCAPTION 启用系统拖拽
                    handled = true;
                    return (IntPtr)2; // HTCAPTION
                }
            }

            // 折叠态：判断是否在小横条区域
            if (_viewModel.CurrentState == SidebarState.Collapsed)
            {
                // 小横条区域：X ∈ [0, 12]（或 Hover 时 [0, 16]）
                double barWidth = _viewModel.IsHovering ? 16 : 12;
                if (windowPoint.X >= 0 && windowPoint.X <= barWidth)
                {
                    // 小横条区域，返回 HTCLIENT 让 WPF 处理点击/悬停
                    handled = true;
                    return (IntPtr)1; // HTCLIENT
                }

                // 小横条以外的透明区域，返回 HTTRANSPARENT 使点击穿透
                handled = true;
                return (IntPtr)(-1); // HTTRANSPARENT
            }

            return IntPtr.Zero;
        }

        #endregion

        #region ===== 防穿透控制 =====

        /// <summary>
        /// 根据侧边栏状态更新窗口的点击穿透行为。
        /// 
        /// 折叠态（Collapsed）：
        ///   启用 WS_EX_TRANSPARENT，使窗口透明区域的鼠标点击穿透到下方桌面。
        ///   小横条区域通过 WM_NCHITTEST 返回 HTCLIENT 保持可交互。
        /// 
        /// 展开态（Expanded）：
        ///   禁用 WS_EX_TRANSPARENT，整个面板可正常交互。
        /// 
        /// 注意：WS_EX_TRANSPARENT 是窗口级别的设置，会影响整个窗口。
        /// 我们通过 WM_NCHITTEST 的细粒度命中测试来弥补这一限制：
        ///   - 折叠态时，WS_EX_TRANSPARENT 使整个窗口默认穿透
        ///   - 但 WM_NCHITTEST 先于穿透判断执行，在小横条区域返回 HTCLIENT
        ///   - 这样小横条区域仍然可以接收鼠标消息
        /// </summary>
        private void UpdateClickThrough(SidebarState state)
        {
            if (_hwnd == IntPtr.Zero) return;

            switch (state)
            {
                case SidebarState.Collapsed:
                    // 折叠态：启用穿透（透明区域点击穿透到下方桌面）
                    NativeMethods.SetClickThrough(_hwnd, enable: true);
                    break;

                case SidebarState.Expanded:
                    // 展开态：禁用穿透（整个面板可交互）
                    NativeMethods.SetClickThrough(_hwnd, enable: false);
                    break;
            }
        }

        #endregion

        #region ===== 动画控制 =====

        /// <summary>
        /// 小横条点击事件：触发展开动画。
        /// </summary>
        private void OnCollapsedBarClick(object sender, MouseButtonEventArgs e)
        {
            if (_isAnimating || _viewModel.CurrentState == SidebarState.Expanded) return;
            ExpandSidebar();
        }

        /// <summary>
        /// 收回按钮点击事件：触发收回动画。
        /// </summary>
        private void OnCollapseButtonClick(object sender, RoutedEventArgs e)
        {
            if (_isAnimating || _viewModel.CurrentState == SidebarState.Collapsed) return;
            CollapseSidebar();
        }

        /// <summary>
        /// 执行展开动画。
        /// 1. 先禁用防穿透（面板需要可交互）
        /// 2. 播放滑出动画（TranslateTransform.X: 0 → -320）
        /// 3. 动画完成后更新 ViewModel 状态
        /// </summary>
        private void ExpandSidebar()
        {
            _isAnimating = true;

            // 展开前先禁用穿透，使面板可交互
            UpdateClickThrough(SidebarState.Expanded);

            // 播放展开动画
            var storyboard = (Storyboard)FindResource("ExpandStoryboard");
            storyboard.Begin();
        }

        /// <summary>
        /// 执行收回动画。
        /// 1. 播放滑回动画（TranslateTransform.X: -320 → 0）
        /// 2. 动画完成后启用防穿透（透明区域穿透到下方桌面）
        /// 3. 更新 ViewModel 状态
        /// </summary>
        private void CollapseSidebar()
        {
            _isAnimating = true;

            // 播放收回动画
            var storyboard = (Storyboard)FindResource("CollapseStoryboard");
            storyboard.Begin();
        }

        /// <summary>
        /// 展开动画完成回调。
        /// 更新 ViewModel 状态为 Expanded。
        /// </summary>
        private void OnExpandCompleted(object? sender, EventArgs e)
        {
            _isAnimating = false;
            _viewModel.CurrentState = SidebarState.Expanded;
        }

        /// <summary>
        /// 收回动画完成回调。
        /// 更新 ViewModel 状态为 Collapsed，并启用防穿透。
        /// </summary>
        private void OnCollapseCompleted(object? sender, EventArgs e)
        {
            _isAnimating = false;
            _viewModel.CurrentState = SidebarState.Collapsed;

            // 收回完成后启用穿透
            UpdateClickThrough(SidebarState.Collapsed);
        }

        #endregion

        #region ===== Hover 动画 =====

        /// <summary>
        /// 鼠标进入小横条区域：播放 Hover 放大动画 + 更新 ViewModel 状态。
        /// </summary>
        private void OnCollapsedBarMouseEnter(object sender, MouseEventArgs e)
        {
            _viewModel.IsHovering = true;
            var storyboard = (Storyboard)FindResource("HoverInStoryboard");
            storyboard.Begin();
        }

        /// <summary>
        /// 鼠标离开小横条区域：播放 Hover 缩小动画 + 更新 ViewModel 状态。
        /// </summary>
        private void OnCollapsedBarMouseLeave(object sender, MouseEventArgs e)
        {
            _viewModel.IsHovering = false;
            var storyboard = (Storyboard)FindResource("HoverOutStoryboard");
            storyboard.Begin();
        }

        #endregion

        #region ===== 失焦自动收回（核心难点） =====

        /// <summary>
        /// 窗口失活（Deactivated）事件处理。
        /// 
        /// 当用户在面板展开状态下，点击面板区域之外的任何屏幕位置时，
        /// Windows 会将焦点转移到其他窗口，触发 Deactivated 事件。
        /// 我们在此事件中触发收回动画，实现"点击外部自动收回"的交互。
        /// 
        /// 注意事项：
        /// - 必须检查当前状态为 Expanded 才收回，避免折叠态误触发
        /// - 必须检查 _isAnimating，避免动画期间重复触发
        /// - 收回动画期间窗口会短暂获得焦点（动画开始时），需要防止递归
        /// </summary>
        private void OnDeactivated(object? sender, EventArgs e)
        {
            if (_viewModel.CurrentState == SidebarState.Expanded && !_isAnimating)
            {
                CollapseSidebar();
            }
        }

        #endregion

        #region ===== 标题栏拖拽 =====

        /// <summary>
        /// 标题栏鼠标左键按下事件。
        /// 通过调用 Window.DragMove 启动窗口拖拽。
        /// 
        /// 注意：WndProc 中的 HTCAPTION 返回值已实现系统原生拖拽，
        /// 此方法作为备用方案。当 WM_NCHITTEST 处理不当时可使用。
        /// </summary>
        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        #endregion

        #region ===== 锁屏桌面挂载 =====

        /// <summary>
        /// 安装系统事件钩子，监听桌面切换事件。
        /// 
        /// 原理：
        /// Windows 在锁屏时会从默认桌面（Default）切换到 Winlogon 桌面。
        /// 普通应用窗口只在 Default 桌面显示，锁屏后不可见。
        /// 通过监听 EVENT_SYSTEM_DESKTOPSWITCH 事件，我们可以在桌面切换时
        /// 尝试将当前线程绑定到 Winlogon 桌面，使侧边栏在锁屏界面也可见。
        /// 
        /// 注意：
        /// - 此功能需要管理员权限才能访问 Winlogon 桌面
        /// - _winEventProc 必须保持引用，否则 GC 会回收委托导致钩子失效
        /// - 回调在 UI 线程执行（WINEVENT_OUTOFCONTEXT），可直接操作 UI
        /// </summary>
        private void InstallWinEventHook()
        {
            _winEventProc = new NativeMethods.WinEventDelegate(OnDesktopSwitch);

            _winEventHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_DESKTOPSWITCH,  // eventMin
                NativeMethods.EVENT_SYSTEM_DESKTOPSWITCH,  // eventMax
                IntPtr.Zero,                                // hmodWinEventProc
                _winEventProc,                              // 回调委托
                0,                                          // idProcess（所有进程）
                0,                                          // idThread（所有线程）
                NativeMethods.WINEVENT_OUTOFCONTEXT       // 回调在调用线程执行
            );
        }

        /// <summary>
        /// 卸载系统事件钩子。
        /// </summary>
        private void UninstallWinEventHook()
        {
            if (_winEventHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
        }

        /// <summary>
        /// 桌面切换事件回调。
        /// 
        /// 当系统切换桌面时（包括锁屏/解锁），此回调被触发。
        /// 我们尝试打开 Winlogon 桌面并将窗口挂载上去，使其在锁屏界面可见。
        /// 
        /// 技术细节：
        /// 1. OpenDesktop("Winlogon") 打开锁屏桌面句柄
        /// 2. SetThreadDesktop 将当前线程绑定到 Winlogon 桌面
        /// 3. 之后该线程创建/管理的窗口会显示在锁屏界面
        /// 4. 使用完毕后 CloseDesktop 关闭句柄
        /// 
        /// 注意：SetThreadDesktop 要求调用线程尚未在任何桌面上创建窗口，
        /// 因此在实际生产环境中，通常需要创建专用的后台线程来管理锁屏窗口。
        /// 此处为简化实现，仅做基础挂载尝试。
        /// </summary>
        private void OnDesktopSwitch(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            try
            {
                // 尝试打开 Winlogon 桌面
                IntPtr hDesktop = NativeMethods.OpenDesktop(
                    "Winlogon",
                    0,
                    false,
                    NativeMethods.DESKTOP_READOBJECTS |
                    NativeMethods.DESKTOP_WRITEOBJECTS |
                    NativeMethods.DESKTOP_SWITCHDESKTOP
                );

                if (hDesktop != IntPtr.Zero)
                {
                    // 将当前线程绑定到 Winlogon 桌面
                    NativeMethods.SetThreadDesktop(hDesktop);

                    // 重新设置窗口置顶，确保在锁屏界面也可见
                    if (_hwnd != IntPtr.Zero)
                    {
                        NativeMethods.SetTopmost(_hwnd);
                    }

                    // 关闭桌面句柄
                    NativeMethods.CloseDesktop(hDesktop);
                }
            }
            catch
            {
                // 锁屏桌面挂载失败时静默忽略
                // 此功能需要管理员权限，普通权限下会失败
            }
        }

        #endregion

        #region ===== 窗口关闭清理 =====

        protected override void OnClosed(EventArgs e)
        {
            UninstallWinEventHook();
            _hwndSource?.RemoveHook(WndProc);
            base.OnClosed(e);
        }

        #endregion
    }
}
