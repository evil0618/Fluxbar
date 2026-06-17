using System.Windows;
using Microsoft.Win32;

namespace GlobalSidebar
{
    /// <summary>
    /// 设置窗口：允许用户配置侧边栏参数。
    /// </summary>
    public partial class SettingsWindow : Window
    {
        /// <summary>注册表自启动键路径</summary>
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "GlobalSidebar";

        public SettingsWindow()
        {
            InitializeComponent();

            // 绑定滑块值变化事件
            SidebarWidthSlider.ValueChanged += (s, e) =>
                SidebarWidthText.Text = ((int)SidebarWidthSlider.Value).ToString();
            BarWidthSlider.ValueChanged += (s, e) =>
                BarWidthText.Text = ((int)BarWidthSlider.Value).ToString();

            // 读取当前自启动状态
            AutoStartCheckBox.IsChecked = IsAutoStartEnabled();
        }

        /// <summary>
        /// 检查是否已启用开机自启动。
        /// </summary>
        private bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 设置开机自启动。
        /// </summary>
        private void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key == null) return;

                if (enable)
                {
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (exePath != null)
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch
            {
                // 注册表操作失败时静默忽略
            }
        }

        /// <summary>
        /// 确定按钮：保存设置并关闭窗口。
        /// </summary>
        private void OnOkButtonClick(object sender, RoutedEventArgs e)
        {
            // 保存自启动设置
            SetAutoStart(AutoStartCheckBox.IsChecked == true);

            // TODO: 保存其他设置（侧边栏宽度、横条宽度等）到配置文件
            // 当前仅保存自启动，其他设置后续扩展

            Close();
        }

        /// <summary>
        /// 取消按钮：放弃更改并关闭窗口。
        /// </summary>
        private void OnCancelButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
