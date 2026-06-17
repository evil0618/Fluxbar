using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Drawing;

namespace GlobalSidebar
{
    /// <summary>
    /// App 类：应用程序入口，负责单例模式、托盘图标与启动流程。
    /// </summary>
    public partial class App : Application
    {
        /// <summary>命名 Mutex，用于单实例检测</summary>
        private Mutex? _singleInstanceMutex;

        /// <summary>Mutex 名称，使用 GUID 确保唯一性</summary>
        private const string MutexName = @"Global\GlobalSidebar_SingleInstance_{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}";

        /// <summary>系统托盘图标</summary>
        private NotifyIcon? _notifyIcon;

        /// <summary>主窗口引用</summary>
        private MainWindow? _mainWindow;

        /// <summary>设置窗口引用</summary>
        private SettingsWindow? _settingsWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 单实例检测
            _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "GlobalSidebar 已在运行中，不可重复启动。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                _singleInstanceMutex.Dispose();
                Shutdown();
                return;
            }

            // 启用硬件加速渲染
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.Default;

            base.OnStartup(e);

            // 初始化托盘图标
            InitNotifyIcon();

            // 创建并显示主窗口（侧边栏）
            _mainWindow = new MainWindow();
            _mainWindow.Show();
        }

        /// <summary>
        /// 初始化系统托盘图标。
        /// 左键单击打开设置，右键显示上下文菜单。
        /// </summary>
        private void InitNotifyIcon()
        {
            _notifyIcon = new NotifyIcon();

            // 使用系统应用图标
            _notifyIcon.Icon = SystemIcons.Application;
            _notifyIcon.Text = "GlobalSidebar";
            _notifyIcon.Visible = true;

            // 左键单击打开设置
            _notifyIcon.MouseClick += OnNotifyIconMouseClick;

            // 右键上下文菜单
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();

            var settingsItem = new System.Windows.Forms.ToolStripMenuItem("设置");
            settingsItem.Click += (s, args) => ShowSettings();
            contextMenu.Items.Add(settingsItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitItem = new System.Windows.Forms.ToolStripMenuItem("退出");
            exitItem.Click += (s, args) => ExitApp();
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        /// <summary>
        /// 托盘图标鼠标点击事件。
        /// 左键单击打开设置窗口。
        /// </summary>
        private void OnNotifyIconMouseClick(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                ShowSettings();
            }
        }

        /// <summary>
        /// 显示设置窗口。如果已打开则激活已有窗口。
        /// </summary>
        private void ShowSettings()
        {
            if (_settingsWindow == null || !_settingsWindow.IsLoaded)
            {
                _settingsWindow = new SettingsWindow();
                _settingsWindow.Show();
            }
            else
            {
                _settingsWindow.Activate();
            }
        }

        /// <summary>
        /// 退出应用程序。
        /// </summary>
        private void ExitApp()
        {
            _notifyIcon?.Dispose();
            _mainWindow?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _notifyIcon?.Dispose();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
