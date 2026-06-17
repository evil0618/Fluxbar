using System;
using System.Threading;
using System.Windows;

namespace GlobalSidebar
{
    /// <summary>
    /// App 类：应用程序入口，负责单例模式与启动流程。
    /// 
    /// 单例模式实现：
    /// 使用命名 Mutex（互斥体）防止应用多开。
    /// Mutex 是操作系统级别的同步原语，即使多个进程同时启动，
    /// 也只有一个能成功获取 Mutex 所有权。
    /// 
    /// 命名规则："Global\" 前缀使 Mutex 在全局命名空间可见（跨会话），
    /// 后跟 GUID 确保唯一性。
    /// </summary>
    public partial class App : Application
    {
        /// <summary>命名 Mutex，用于单实例检测</summary>
        private Mutex? _singleInstanceMutex;

        /// <summary>Mutex 名称，使用 GUID 确保唯一性</summary>
        private const string MutexName = @"Global\GlobalSidebar_SingleInstance_{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}";

        /// <summary>
        /// 应用启动时执行。
        /// 检查是否已有实例运行，如有则退出当前实例。
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            // 尝试创建命名 Mutex
            // createdNew=true 表示当前是第一个获取 Mutex 的实例
            // createdNew=false 表示已有其他实例持有 Mutex
            _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                // 已有实例运行，提示并退出
                MessageBox.Show(
                    "GlobalSidebar 已在运行中，不可重复启动。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                // 释放 Mutex 并退出
                _singleInstanceMutex.Dispose();
                Shutdown();
                return;
            }

            // 启用硬件加速渲染
            // RenderOptions.ProcessRenderMode 默认为 Default（自动选择）
            // 确保不设置为 SoftwareOnly
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.Default;

            base.OnStartup(e);
        }

        /// <summary>
        /// 应用退出时释放 Mutex。
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            // 释放 Mutex 所有权，允许新实例启动
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();

            base.OnExit(e);
        }
    }
}
