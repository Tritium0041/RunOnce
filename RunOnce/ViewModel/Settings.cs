/*
 * 设置页面 ViewModel
 * 管理设置页面的所有可绑定状态、选项列表与业务逻辑，向 View 暴露配置数据的读写接口
 *
 * @author: WaterRun
 * @file: ViewModel/Settings.cs
 * @date: 2026-03-10
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
/// 不变量：所有可变属性的 Setter 在非抑制状态下同步写入 <see cref="Config"/>（脚本放置行为除外，需 View 确认后写入）；
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

    /// <summary>
    /// 主题风格 ComboBox 的显示选项列表。
    /// </summary>
    private readonly ObservableCollection<string> _themeOptions;

    /// <summary>
    /// 显示语言 ComboBox 的显示选项列表。
    /// </summary>
    private readonly ObservableCollection<string> _languageOptions;

    /// <summary>
    /// 语言选择框模式 ComboBox 的显示选项列表。
    /// </summary>
    private readonly ObservableCollection<string> _selectorModeOptions;

    /// <summary>
    /// 命令解释器类型 ComboBox 的显示选项列表。
    /// </summary>
    private readonly ObservableCollection<string> _shellOptions;

    /// <summary>
    /// 脚本放置行为 ComboBox 的显示选项列表。
    /// </summary>
    private readonly ObservableCollection<string> _scriptPlacementOptions;

    #endregion

    #region 选中索引后备字段

    /// <summary>
    /// 当前选中的主题风格索引。
    /// </summary>
    private int _selectedThemeIndex;

    /// <summary>
    /// 当前选中的显示语言索引。
    /// </summary>
    private int _selectedLanguageIndex;

    /// <summary>
    /// 当前选中的语言选择框模式索引。
    /// </summary>
    private int _selectedSelectorModeIndex;

    /// <summary>
    /// 当前选中的命令解释器类型索引。
    /// </summary>
    private int _selectedShellIndex;

    /// <summary>
    /// 当前选中的脚本放置行为索引。
    /// </summary>
    private int _selectedScriptPlacementIndex;

    #endregion

    #region 开关后备字段

    /// <summary>
    /// 执行前确认开关状态。
    /// </summary>
    private bool _confirmBeforeExecution;

    /// <summary>
    /// 执行时自动退出开关状态。
    /// </summary>
    private bool _autoExitOnExecution;

    /// <summary>
    /// 运行完毕后自动关闭终端开关状态。
    /// </summary>
    private bool _autoCloseTerminalOnCompletion;

    #endregion

    #region 事件

    /// <summary>
    /// 属性值变更时触发的事件。
    /// </summary>
    /// <remarks>
    /// 触发时机：调用 <see cref="SetProperty{T}"/> 且新旧值不相等时，或显式调用 <see cref="OnPropertyChanged"/>。
    /// 线程上下文：在调用线程触发，通常为 UI 线程。
    /// </remarks>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 用户更改主题风格时触发，参数为新选中的主题值。
    /// </summary>
    /// <remarks>
    /// 触发时机：用户通过 ComboBox 选择新主题后，Config 已更新。
    /// 线程上下文：UI 线程。
    /// </remarks>
    public event Action<ThemeStyle>? ThemeChanged;

    /// <summary>
    /// 用户更改显示语言时触发。
    /// </summary>
    /// <remarks>
    /// 触发时机：用户通过 ComboBox 选择新语言后，Config 已更新。
    /// View 应在此事件中调用 <see cref="RefreshAfterLanguageChange"/> 并刷新本地化文本。
    /// 线程上下文：UI 线程。
    /// </remarks>
    public event Action? LanguageChanged;

    /// <summary>
    /// 用户更改脚本放置行为时触发，参数为旧索引与新索引。
    /// </summary>
    /// <remarks>
    /// 触发时机：用户通过 ComboBox 选择新放置行为后，Config 尚未更新。
    /// View 应在此事件中弹出确认对话框，确认后调用 <see cref="ConfirmScriptPlacement"/>，
    /// 取消后调用 <see cref="RevertScriptPlacement"/>。
    /// 线程上下文：UI 线程。
    /// </remarks>
    public event Action<int, int>? ScriptPlacementChangeRequested;

    #endregion

    /// <summary>
    /// 初始化设置 ViewModel 实例，从 <see cref="Config"/> 加载当前配置并构建选项列表。
    /// </summary>
    public SettingsViewModel()
    {
        _themeOptions = new(Enum.GetValues<ThemeStyle>().Select(Config.GetThemeDisplayName));
        _languageOptions = new(Enum.GetValues<DisplayLanguage>().Select(Config.GetLanguageDisplayName));
        _selectorModeOptions = new(Enum.GetValues<LanguageSelectorMode>().Select(Config.GetSelectorModeDisplayName));
        _shellOptions = new(Enum.GetValues<ShellType>().Select(Config.GetShellDisplayName));
        _scriptPlacementOptions = new(Enum.GetValues<ScriptPlacementBehavior>().Select(Config.GetScriptPlacementDisplayName));

        _isSuppressingChanges = true;
        SynchronizeFromConfig();
        _isSuppressingChanges = false;
    }

    #region 选项列表属性

    /// <summary>
    /// 主题风格 ComboBox 的本地化显示选项列表。
    /// </summary>
    public ObservableCollection<string> ThemeOptions => _themeOptions;

    /// <summary>
    /// 显示语言 ComboBox 的本地化显示选项列表。
    /// </summary>
    public ObservableCollection<string> LanguageOptions => _languageOptions;

    /// <summary>
    /// 语言选择框模式 ComboBox 的本地化显示选项列表。
    /// </summary>
    public ObservableCollection<string> SelectorModeOptions => _selectorModeOptions;

    /// <summary>
    /// 命令解释器类型 ComboBox 的本地化显示选项列表。
    /// </summary>
    public ObservableCollection<string> ShellOptions => _shellOptions;

    /// <summary>
    /// 脚本放置行为 ComboBox 的本地化显示选项列表。
    /// </summary>
    public ObservableCollection<string> ScriptPlacementOptions => _scriptPlacementOptions;

    #endregion

    #region 选中索引属性

    /// <summary>
    /// 主题风格 ComboBox 的当前选中索引。
    /// </summary>
    /// <value>对应 <see cref="ThemeStyle"/> 枚举的整型值。设置时同步写入 Config 并触发 <see cref="ThemeChanged"/>。</value>
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
    /// <value>对应 <see cref="DisplayLanguage"/> 枚举的整型值。设置时同步写入 Config 并触发 <see cref="LanguageChanged"/>。</value>
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
    /// 语言选择框模式 ComboBox 的当前选中索引。
    /// </summary>
    /// <value>对应 <see cref="LanguageSelectorMode"/> 枚举的整型值。设置时同步写入 Config。</value>
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
    /// <value>对应 <see cref="ShellType"/> 枚举的整型值。设置时同步写入 Config。</value>
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
    /// <value>
    /// 对应 <see cref="ScriptPlacementBehavior"/> 枚举的整型值。
    /// 设置时不直接写入 Config，而是触发 <see cref="ScriptPlacementChangeRequested"/> 事件，
    /// 由 View 层弹出确认对话框后决定是否写入。
    /// </value>
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

    #region 脚本放置行为确认/撤销

    /// <summary>
    /// 确认脚本放置行为变更，将新值写入 Config 持久化。
    /// </summary>
    /// <param name="newIndex">已确认的新选项索引。</param>
    /// <remarks>
    /// 由 View 层在用户确认对话框后调用。
    /// </remarks>
    public void ConfirmScriptPlacement(int newIndex)
    {
        Config.ScriptPlacement = (ScriptPlacementBehavior)newIndex;
    }

    /// <summary>
    /// 撤销脚本放置行为变更，将 ComboBox 恢复到原选项。
    /// </summary>
    /// <param name="oldIndex">变更前的选项索引。</param>
    /// <remarks>
    /// 由 View 层在用户取消对话框后调用。
    /// 在抑制状态下设置索引，避免再次触发确认事件。
    /// </remarks>
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
    /// <value>true 表示需要确认，false 表示直接执行。设置时同步写入 Config。</value>
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
    /// <value>true 表示自动退出，false 表示保持运行。设置时同步写入 Config。</value>
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
    /// <value>true 表示自动关闭终端，false 表示保留终端窗口。设置时同步写入 Config。</value>
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

    /// <summary>
    /// 本地化的应用程序显示名称。
    /// </summary>
    public string AppName => Config.AppName;

    /// <summary>
    /// 应用程序版本号。
    /// </summary>
    public string Version => Config.Version;

    /// <summary>
    /// 带前缀的版本号显示文本。
    /// </summary>
    public string VersionDisplay => $"v{Config.Version}";

    /// <summary>
    /// 应用程序作者名称。
    /// </summary>
    public string Author => Config.Author;

    /// <summary>
    /// 格式化的编译时间文本。
    /// </summary>
    public string BuildTimeText => _buildTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// GitHub 仓库 URL。
    /// </summary>
    public string GitHubUrl => Config.GitHubUrl;

    /// <summary>
    /// 微软商店 URL 是否可用。
    /// </summary>
    public bool HasStoreUrl => !string.IsNullOrEmpty(Config.MicrosoftStoreUrl);

    /// <summary>
    /// 微软商店 URL。
    /// </summary>
    public string StoreUrl => Config.MicrosoftStoreUrl;

    #endregion

    #region 高级设置访问方法

    /// <summary>
    /// 获取当前配置的临时文件名前缀。
    /// </summary>
    /// <returns>当前临时文件名前缀字符串。</returns>
    public string GetTempFilePrefix() => Config.TempFilePrefix;

    /// <summary>
    /// 获取当前配置的置信度阈值。
    /// </summary>
    /// <returns>当前置信度阈值，范围 [0.0, 1.0]。</returns>
    public double GetConfidenceThreshold() => Config.ConfidenceThreshold;

    /// <summary>
    /// 获取所有语言执行命令的当前配置副本。
    /// </summary>
    /// <returns>语言标识符到执行命令的字典副本。</returns>
    public Dictionary<string, string> GetAllLanguageCommands() => Config.GetAllLanguageCommands();

    /// <summary>
    /// 保存高级设置到持久化配置。
    /// </summary>
    /// <param name="prefix">临时文件名前缀，不允许为 null 或空白字符串。</param>
    /// <param name="threshold">置信度阈值，范围 [0.0, 1.0]。</param>
    /// <param name="commands">语言执行命令字典，不允许为 null。</param>
    /// <exception cref="ArgumentNullException">当 prefix 或 commands 为 null 时抛出。</exception>
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

    /// <summary>
    /// 将高级设置重置为默认值并返回默认值。
    /// </summary>
    /// <returns>包含默认临时文件前缀、默认置信度阈值和默认语言命令的元组。</returns>
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
    /// <remarks>
    /// 调用 <see cref="Config.ResetAllSettings"/> 后原地刷新选项文本并同步控件状态。
    /// 调用方需额外处理主题应用与 UI 刷新。
    /// </remarks>
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
    /// <remarks>
    /// 通过 <see cref="ObservableCollection{T}"/> 的原地更新机制刷新选项显示文本。
    /// 因 WinUI 3 ComboBox 在 Replace 选中项时会将 SelectedIndex 重置为 -1，
    /// 故在抑制状态下重新同步索引以恢复正确选中。
    /// </remarks>
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

    /// <summary>
    /// 触发指定属性的变更通知。
    /// </summary>
    /// <param name="propertyName">变更的属性名称，由编译器自动填充。</param>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 设置属性后备字段的值，若值发生变化则触发变更通知。
    /// </summary>
    /// <typeparam name="T">属性值的类型。</typeparam>
    /// <param name="field">属性的后备字段引用。</param>
    /// <param name="value">待设置的新值。</param>
    /// <param name="propertyName">属性名称，由编译器自动填充。</param>
    /// <returns>若值发生变化并已通知则返回 true，否则返回 false。</returns>
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
    /// 原地更新所有 ComboBox 选项列表的显示文本，保持集合引用与选中索引不变。
    /// </summary>
    private void RefreshOptionTexts()
    {
        UpdateCollectionItems(_themeOptions, Enum.GetValues<ThemeStyle>().Select(Config.GetThemeDisplayName));
        UpdateCollectionItems(_languageOptions, Enum.GetValues<DisplayLanguage>().Select(Config.GetLanguageDisplayName));
        UpdateCollectionItems(_selectorModeOptions, Enum.GetValues<LanguageSelectorMode>().Select(Config.GetSelectorModeDisplayName));
        UpdateCollectionItems(_shellOptions, Enum.GetValues<ShellType>().Select(Config.GetShellDisplayName));
        UpdateCollectionItems(_scriptPlacementOptions, Enum.GetValues<ScriptPlacementBehavior>().Select(Config.GetScriptPlacementDisplayName));
    }

    /// <summary>
    /// 原地更新 <see cref="ObservableCollection{T}"/> 中的元素，仅替换发生变化的项。
    /// </summary>
    /// <param name="collection">待更新的目标集合。</param>
    /// <param name="newItems">新的元素序列，长度应与集合相同。</param>
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
        SelectedSelectorModeIndex = (int)Config.SelectorMode;
        SelectedShellIndex = (int)Config.Shell;
        SelectedScriptPlacementIndex = (int)Config.ScriptPlacement;
        ConfirmBeforeExecution = Config.ConfirmBeforeExecution;
        AutoExitOnExecution = Config.AutoExitOnExecution;
        AutoCloseTerminalOnCompletion = Config.AutoCloseTerminalOnCompletion;
    }

    /// <summary>
    /// 获取程序集的编译时间。
    /// </summary>
    /// <returns>编译时间的 <see cref="DateTime"/> 表示，若无法获取则返回当前时间。</returns>
    private static DateTime RetrieveBuildTime()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        return TryGetBuildTimeFromVersion(assembly)
               ?? TryGetBuildTimeFromFile(assembly)
               ?? DateTime.Now;
    }

    /// <summary>
    /// 尝试从程序集版本的 InformationalVersion 属性解析编译时间戳。
    /// </summary>
    /// <param name="assembly">目标程序集，不允许为 null。</param>
    /// <returns>解析成功时返回编译时间，否则返回 null。</returns>
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

    /// <summary>
    /// 尝试从程序集文件的最后修改时间获取编译时间。
    /// </summary>
    /// <param name="assembly">目标程序集，不允许为 null。</param>
    /// <returns>文件存在时返回最后修改时间，否则返回 null。</returns>
    private static DateTime? TryGetBuildTimeFromFile(Assembly assembly)
    {
        string? filePath = assembly.Location;
        return !string.IsNullOrEmpty(filePath) && File.Exists(filePath)
            ? File.GetLastWriteTime(filePath)
            : null;
    }

    #endregion
}
