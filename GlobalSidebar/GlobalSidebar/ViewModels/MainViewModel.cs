using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace GlobalSidebar.ViewModels
{
    /// <summary>
    /// 侧边栏状态枚举。
    /// Collapsed = 折叠态（小横条），Expanded = 展开态（功能面板）。
    /// </summary>
    public enum SidebarState
    {
        Collapsed,
        Expanded
    }

    /// <summary>
    /// 主窗口 ViewModel，采用 MVVM 模式管理侧边栏的展开/折叠状态。
    /// 所有 UI 状态变更均通过数据绑定驱动，严禁在 Code-Behind 中直接操作 UI。
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        #region ===== 私有字段 =====

        private SidebarState _currentState = SidebarState.Collapsed;
        private bool _isHovering;
        private double _sidebarOffsetX; // TranslateTransform.X 的当前值，供动画绑定

        #endregion

        #region ===== 公开属性 =====

        /// <summary>
        /// 当前侧边栏状态（折叠/展开）。
        /// 状态变更时触发 PropertyChanged 通知，驱动 UI 更新。
        /// </summary>
        public SidebarState CurrentState
        {
            get => _currentState;
            set
            {
                if (_currentState != value)
                {
                    _currentState = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsExpanded));
                    OnPropertyChanged(nameof(IsCollapsed));

                    // 状态变更时触发命令可执行性刷新
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>是否处于展开态</summary>
        public bool IsExpanded => _currentState == SidebarState.Expanded;

        /// <summary>是否处于折叠态</summary>
        public bool IsCollapsed => _currentState == SidebarState.Collapsed;

        /// <summary>
        /// 鼠标是否悬停在小横条上。
        /// 用于控制 Hover 时的宽度过渡和呼吸灯效果。
        /// </summary>
        public bool IsHovering
        {
            get => _isHovering;
            set
            {
                if (_isHovering != value)
                {
                    _isHovering = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 侧边栏水平偏移量（TranslateTransform.X）。
        /// 折叠态 = 0（小横条贴紧左边缘），展开态 = -ExpandedWidth（面板完全滑入屏幕）。
        /// 注意：此属性仅用于 Code-Behind 中的动画控制，不直接绑定到 XAML。
        /// </summary>
        public double SidebarOffsetX
        {
            get => _sidebarOffsetX;
            set
            {
                if (Math.Abs(_sidebarOffsetX - value) > 0.01)
                {
                    _sidebarOffsetX = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>折叠态小横条默认宽度</summary>
        public double CollapsedWidth => 12.0;

        /// <summary>折叠态小横条 Hover 宽度</summary>
        public double CollapsedHoverWidth => 16.0;

        /// <summary>展开态面板宽度</summary>
        public double ExpandedWidth => 320.0;

        #endregion

        #region ===== 命令 =====

        /// <summary>
        /// 切换侧边栏状态的命令。
        /// 折叠态 → 展开态，展开态 → 折叠态。
        /// </summary>
        public ICommand ToggleCommand { get; }

        /// <summary>
        /// 收回面板命令（失焦时调用）。
        /// 仅在展开态时有效。
        /// </summary>
        public ICommand CollapseCommand { get; }

        #endregion

        #region ===== 构造函数 =====

        public MainViewModel()
        {
            ToggleCommand = new RelayCommand(ExecuteToggle, CanToggle);
            CollapseCommand = new RelayCommand(ExecuteCollapse, CanCollapse);
        }

        #endregion

        #region ===== 命令实现 =====

        /// <summary>
        /// 执行状态切换。
        /// 折叠态 → 展开态：面板从左侧滑出。
        /// 展开态 → 折叠态：面板滑回左侧边缘。
        /// </summary>
        private void ExecuteToggle()
        {
            CurrentState = CurrentState == SidebarState.Collapsed
                ? SidebarState.Expanded
                : SidebarState.Collapsed;
        }

        /// <summary>始终可切换</summary>
        private bool CanToggle() => true;

        /// <summary>
        /// 执行收回操作。仅在展开态时有效。
        /// </summary>
        private void ExecuteCollapse()
        {
            if (CurrentState == SidebarState.Expanded)
            {
                CurrentState = SidebarState.Collapsed;
            }
        }

        /// <summary>仅在展开态时可收回</summary>
        private bool CanCollapse() => CurrentState == SidebarState.Expanded;

        #endregion

        #region ===== INotifyPropertyChanged =====

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// 通用命令实现，支持可执行性判断。
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
