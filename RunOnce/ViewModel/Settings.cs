/*
 * 设置页面 ViewModel
 * 管理设置页面的所有可绑定状态、选项列表与业务逻辑，向 View 暴露配置数据的读写接口
 *
 * @author: WaterRun
 * @file: ViewModel/Settings.cs
 * @date: 2026-03-19
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using RunOnce.Static;

namespace RunOnce.ViewModel;

/// <summary>
/// 设置页面的 ViewModel，承载所有用户可交互设置的状态及关于信息。
/// </summary>
/// <remarks>
/// 不变量：所有可变属性的 Setter 在非抑制状态下同步写入 <see cref="Config"/>（脚本放置行为和编辑器性能策略除外，需 View 确认后写入）；
/// 选项列表使用 <see cref="ObservableCollection{T}"/> 实现原地更新，避免语言切换时的布局抖动。
/// 线程安全：非线程安全，所有成员必须在 UI 线程访问。
/// 副作用：属性 Setter 会触发 <see cref="Config"/> 的持久化写入及 PropertyChanged 通知。
/// </remarks>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// 应用程序编译时间戳，静态缓存避免重复计算。
    /// </summary>
    private static readonly DateTime _buildTime = RetrieveBuildTime();

    /// <summary>
    /// 标识是否正在进行程序化更新以抑制 Config 回写，防止事件循环。
    /// </summary>
    private bool _isSuppressingChanges;

    #region 选项列表（ObservableCollection 实现原地更新）

    private readonly ObservableCollection<string> _themeOptions;
    private readonly ObservableCollection<string> _languageOptions;
    private readonly ObservableCollection<string> _performanceOptions;
    private readonly ObservableCollection<string> _selectorModeOptions;
    private readonly ObservableCollection<string> _shellOptions;
    private readonly ObservableCollection<string> _scriptPlacementOptions;

    #endregion

    #region 选中索引后备字段

    private int _selectedThemeIndex;
    private int _selectedLanguageIndex;
    private int _selectedPerformanceIndex;
    private int _selectedSelectorModeIndex;
    private int _selectedShellIndex;
    private int _selectedScriptPlacementIndex;

    #endregion

    #region 开关后备字段

    private bool _confirmBeforeExecution;
    private bool _autoExitOnExecution;
    private bool _autoCloseTerminalOnCompletion;

    #endregion

    #region 事件

    /// <summary>
    /// 属性值变更时触发的事件。
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 用户更改主题风格时触发。
    /// </summary>
    public event Action<ThemeStyle>? ThemeChanged;

    /// <summary>
    /// 用户更改显示语言时触发。
    /// </summary>
    public event Action? LanguageChanged;

    /// <summary>
    /// 用户更改脚本放置行为时触发，参数为旧索引与新索引。
    /// </summary>
    public event Action<int, int>? ScriptPlacementChangeRequested;

    /// <summary>
    /// 用户更改编辑器性能策略时触发，参数为旧索引与新索引。
    /// </summary>
    /// <remarks>
    /// View 层应在此事件中弹出确认对话框，确认后调用 <see cref="ConfirmPerformanceChange"/>，
    /// 取消后调用 <see cref="RevertPerformanceChange"/>。
    /// </remarks>
    public event Action<int, int>? PerformanceChangeRequested;

    #endregion

    /// <summary>
    /// 初始化设置 ViewModel 实例。
    /// </summary>
    public SettingsViewModel()
    {
        _themeOptions = new(Enum.GetValues<ThemeStyle>().Select(Config.GetThemeDisplayName));
        _languageOptions = new(Enum.GetValues<DisplayLanguage>().Select(Config.GetLanguageDisplayName));
        _performanceOptions = new(Enum.GetValues<EditorPerformance>().Select(Config.GetPerformanceDisplayName));
        _selectorModeOptions = new(Enum.GetValues<LanguageSelectorMode>().Select(Config.GetSelectorModeDisplayName));
        _shellOptions = new(Enum.GetValues<ShellType>().Select(Config.GetShellDisplayName));
        _scriptPlacementOptions = new(Enum.GetValues<ScriptPlacementBehavior>().Select(Config.GetScriptPlacementDisplayName));

        _isSuppressingChanges = true;
        SynchronizeFromConfig();
        _isSuppressingChanges = false;
    }

    #region 选项列表属性

    public ObservableCollection<string> ThemeOptions => _themeOptions;
    public ObservableCollection<string> LanguageOptions => _languageOptions;
    public ObservableCollection<string> PerformanceOptions => _performanceOptions;
    public ObservableCollection<string> SelectorModeOptions => _selectorModeOptions;
    public ObservableCollection<string> ShellOptions => _shellOptions;
    public ObservableCollection<string> ScriptPlacementOptions => _scriptPlacementOptions;

    #endregion

    #region 选中索引属性

    /// <summary>
    /// 主题风格 ComboBox 的当前选中索引。
    /// </summary>
    public int SelectedThemeIndex
    {
        get => _selectedThemeIndex;
        set
        {
            if (SetProperty(ref _selectedThemeIndex, value) && !_isSuppressingChanges && value >= 0)
            {
                ThemeStyle theme = (ThemeStyle)value;
                Config.Theme = theme;
                ThemeChanged?.Invoke(theme);
            }
        }
    }

    /// <summary>
    /// 显示语言 ComboBox 的当前选中索引。
    /// </summary>
    public int SelectedLanguageIndex
    {
        get => _selectedLanguageIndex;
        set
        {
            if (SetProperty(ref _selectedLanguageIndex, value) && !_isSuppressingChanges && value >= 0)
            {
                Config.Language = (DisplayLanguage)value;
                LanguageChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// 编辑器性能策略 ComboBox 的当前选中索引。
    /// </summary>
    /// <value>
    /// 设置时不直接写入 Config，而是触发 <see cref="PerformanceChangeRequested"/> 事件，
    /// 由 View 层弹出确认对话框后决定是否写入。
    /// </value>
    public int SelectedPerformanceIndex
    {
        get => _selectedPerformanceIndex;
        set
        {
            int previousValue = _selectedPerformanceIndex;
            if (SetProperty(ref _selectedPerformanceIndex, value) && !_isSuppressingChanges && value >= 0)
            {
                PerformanceChangeRequested?.Invoke(previousValue, value);
            }
        }
    }

    /// <summary>
    /// 语言选择框模式 ComboBox 的当前选中索引。
    /// </summary>
    public int SelectedSelectorModeIndex
    {
        get => _selectedSelectorModeIndex;
        set
        {
            if (SetProperty(ref _selectedSelectorModeIndex, value) && !_isSuppressingChanges && value >= 0)
            {
                Config.SelectorMode = (LanguageSelectorMode)value;
            }
        }
    }

    /// <summary>
    /// 命令解释器类型 ComboBox 的当前选中索引。
    /// </summary>
    public int SelectedShellIndex
    {
        get => _selectedShellIndex;
        set
        {
            if (SetProperty(ref _selectedShellIndex, value) && !_isSuppressingChanges && value >= 0)
            {
                Config.Shell = (ShellType)value;
            }
        }
    }

    /// <summary>
    /// 脚本放置行为 ComboBox 的当前选中索引。
    /// </summary>
    public int SelectedScriptPlacementIndex
    {
        get => _selectedScriptPlacementIndex;
        set
        {
            int previousValue = _selectedScriptPlacementIndex;
            if (SetProperty(ref _selectedScriptPlacementIndex, value) && !_isSuppressingChanges && value >= 0)
            {
                ScriptPlacementChangeRequested?.Invoke(previousValue, value);
            }
        }
    }

    #endregion

    #region 性能策略确认/撤销

    /// <summary>
    /// 确认编辑器性能策略变更，将新值写入 Config 持久化。
    /// </summary>
    /// <param name="newIndex">已确认的新选项索引。</param>
    public void ConfirmPerformanceChange(int newIndex)
    {
        Config.Performance = (EditorPerformance)newIndex;
    }

    /// <summary>
    /// 撤销编辑器性能策略变更，将 ComboBox 恢复到原选项。
    /// </summary>
    /// <param name="oldIndex">变更前的选项索引。</param>
    public void RevertPerformanceChange(int oldIndex)
    {
        _isSuppressingChanges = true;
        SelectedPerformanceIndex = oldIndex;
        _isSuppressingChanges = false;
    }

    #endregion

    #region 脚本放置行为确认/撤销

    /// <summary>
    /// 确认脚本放置行为变更。
    /// </summary>
    public void ConfirmScriptPlacement(int newIndex)
    {
        Config.ScriptPlacement = (ScriptPlacementBehavior)newIndex;
    }

    /// <summary>
    /// 撤销脚本放置行为变更。
    /// </summary>
    public void RevertScriptPlacement(int oldIndex)
    {
        _isSuppressingChanges = true;
        SelectedScriptPlacementIndex = oldIndex;
        _isSuppressingChanges = false;
    }

    #endregion

    #region 开关属性

    /// <summary>
    /// 执行前是否显示确认对话框。
    /// </summary>
    public bool ConfirmBeforeExecution
    {
        get => _confirmBeforeExecution;
        set
        {
            if (SetProperty(ref _confirmBeforeExecution, value) && !_isSuppressingChanges)
            {
                Config.ConfirmBeforeExecution = value;
            }
        }
    }

    /// <summary>
    /// 开始执行代码时是否自动退出应用程序。
    /// </summary>
    public bool AutoExitOnExecution
    {
        get => _autoExitOnExecution;
        set
        {
            if (SetProperty(ref _autoExitOnExecution, value) && !_isSuppressingChanges)
            {
                Config.AutoExitOnExecution = value;
            }
        }
    }

    /// <summary>
    /// 代码运行完成后是否自动关闭终端窗口。
    /// </summary>
    public bool AutoCloseTerminalOnCompletion
    {
        get => _autoCloseTerminalOnCompletion;
        set
        {
            if (SetProperty(ref _autoCloseTerminalOnCompletion, value) && !_isSuppressingChanges)
            {
                Config.AutoCloseTerminalOnCompletion = value;
            }
        }
    }

    #endregion

    #region 关于信息属性（只读）

    public string AppName => Config.AppName;
    public string Version => Config.Version;
    public string VersionDisplay => $"v{Config.Version}";
    public string Author => Config.Author;
    public string BuildTimeText => _buildTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    public string GitHubUrl => Config.GitHubUrl;
    public bool HasStoreUrl => !string.IsNullOrEmpty(Config.MicrosoftStoreUrl);
    public string StoreUrl => Config.MicrosoftStoreUrl;

    #endregion

    #region 高级设置访问方法

    public string GetTempFilePrefix() => Config.TempFilePrefix;

    public double GetConfidenceThreshold() => Config.ConfidenceThreshold;

    public Dictionary<string, string> GetAllLanguageCommands() => Config.GetAllLanguageCommands();

    public void SaveAdvancedSettings(string prefix, double threshold, Dictionary<string, string> commands)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(commands);

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            Config.TempFilePrefix = prefix;
        }

        if (!double.IsNaN(threshold) && threshold is >= 0.0 and <= 1.0)
        {
            Config.ConfidenceThreshold = threshold;
        }

        foreach ((string language, string command) in commands)
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                Config.SetLanguageCommand(language, command);
            }
        }
    }

    public (string Prefix, double Threshold, Dictionary<string, string> Commands) ResetAdvancedToDefaults()
    {
        Config.TempFilePrefix = Config.DefaultTempFilePrefix;
        Config.ConfidenceThreshold = Config.DefaultConfidenceThreshold;
        Config.ResetAllLanguageCommands();

        return (Config.DefaultTempFilePrefix, Config.DefaultConfidenceThreshold, Config.GetAllLanguageCommands());
    }

    #endregion

    #region 整体重置

    /// <summary>
    /// 将所有设置重置为默认值并同步 ViewModel 状态。
    /// </summary>
    public void ResetAllSettings()
    {
        Config.ResetAllSettings();

        _isSuppressingChanges = true;
        RefreshOptionTexts();
        SynchronizeFromConfig();
        OnPropertyChanged(nameof(AppName));
        _isSuppressingChanges = false;
    }

    #endregion

    #region 语言切换刷新

    /// <summary>
    /// 在显示语言变更后刷新所有本地化相关的 ViewModel 状态。
    /// </summary>
    public void RefreshAfterLanguageChange()
    {
        _isSuppressingChanges = true;
        RefreshOptionTexts();
        SynchronizeFromConfig();
        _isSuppressingChanges = false;
        OnPropertyChanged(nameof(AppName));
    }

    #endregion

    #region INotifyPropertyChanged 实现

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion

    #region 私有辅助方法

    /// <summary>
    /// 原地更新所有 ComboBox 选项列表的显示文本。
    /// </summary>
    private void RefreshOptionTexts()
    {
        UpdateCollectionItems(_themeOptions, Enum.GetValues<ThemeStyle>().Select(Config.GetThemeDisplayName));
        UpdateCollectionItems(_languageOptions, Enum.GetValues<DisplayLanguage>().Select(Config.GetLanguageDisplayName));
        UpdateCollectionItems(_performanceOptions, Enum.GetValues<EditorPerformance>().Select(Config.GetPerformanceDisplayName));
        UpdateCollectionItems(_selectorModeOptions, Enum.GetValues<LanguageSelectorMode>().Select(Config.GetSelectorModeDisplayName));
        UpdateCollectionItems(_shellOptions, Enum.GetValues<ShellType>().Select(Config.GetShellDisplayName));
        UpdateCollectionItems(_scriptPlacementOptions, Enum.GetValues<ScriptPlacementBehavior>().Select(Config.GetScriptPlacementDisplayName));
    }

    private static void UpdateCollectionItems(ObservableCollection<string> collection, IEnumerable<string> newItems)
    {
        int index = 0;
        foreach (string item in newItems)
        {
            if (index < collection.Count)
            {
                if (!string.Equals(collection[index], item, StringComparison.Ordinal))
                {
                    collection[index] = item;
                }
            }

            index++;
        }
    }

    /// <summary>
    /// 从 <see cref="Config"/> 读取当前值并设置到对应的属性。
    /// </summary>
    private void SynchronizeFromConfig()
    {
        SelectedThemeIndex = (int)Config.Theme;
        SelectedLanguageIndex = (int)Config.Language;
        SelectedPerformanceIndex = (int)Config.Performance;
        SelectedSelectorModeIndex = (int)Config.SelectorMode;
        SelectedShellIndex = (int)Config.Shell;
        SelectedScriptPlacementIndex = (int)Config.ScriptPlacement;
        ConfirmBeforeExecution = Config.ConfirmBeforeExecution;
        AutoExitOnExecution = Config.AutoExitOnExecution;
        AutoCloseTerminalOnCompletion = Config.AutoCloseTerminalOnCompletion;
    }

    private static DateTime RetrieveBuildTime()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        return TryGetBuildTimeFromVersion(assembly)
               ?? TryGetBuildTimeFromFile(assembly)
               ?? DateTime.Now;
    }

    private static DateTime? TryGetBuildTimeFromVersion(Assembly assembly)
    {
        string? version = assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;

        if (version is null)
        {
            return null;
        }

        int plusIndex = version.IndexOf('+');
        if (plusIndex < 0 || version.Length <= plusIndex + 14)
        {
            return null;
        }

        string timestampPart = version[(plusIndex + 1)..(plusIndex + 15)];
        return DateTime.TryParseExact(timestampPart, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedTime)
            ? parsedTime
            : null;
    }

    private static DateTime? TryGetBuildTimeFromFile(Assembly assembly)
    {
        string? filePath = assembly.Location;
        return !string.IsNullOrEmpty(filePath) && File.Exists(filePath)
            ? File.GetLastWriteTime(filePath)
            : null;
    }

    #endregion
}