/*
 * 设置页面 ViewModel
 * 管理设置页面的所有可绑定状态、选项列表与业务逻辑，向 View 暴露配置数据的读写接口
 *
 * @author: WaterRun
 * @file: ViewModel/Settings.cs
 * @date: 2026-02-09
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using RunOnce.Static;

namespace RunOnce.ViewModel;

/// <summary>
/// 设置页面的 ViewModel，承载所有用户可交互设置的状态及关于信息。
/// </summary>
/// <remarks>
/// 不变量：所有可变属性的 Setter 在非抑制状态下同步写入 <see cref="Config"/>；选项列表与选中索引始终一致。
/// 线程安全：非线程安全，所有成员必须在 UI 线程访问。
/// 副作用：属性 Setter 会触发 <see cref="Config"/> 的持久化写入及 PropertyChanged 通知。
/// </remarks>
public sealed class SettingsViewModel : ViewModelBase
{
    /// <summary>
    /// 应用程序编译时间戳，静态缓存避免重复计算。
    /// </summary>
    private static readonly DateTime _buildTime = RetrieveBuildTime();

    /// <summary>
    /// 标识是否正在进行程序化更新以抑制 Config 回写，防止事件循环。
    /// </summary>
    private bool _isSuppressingChanges;

    #region 选项列表后备字段

    /// <summary>
    /// 主题风格 ComboBox 的显示选项列表。
    /// </summary>
    private List<string> _themeOptions = [];

    /// <summary>
    /// 显示语言 ComboBox 的显示选项列表。
    /// </summary>
    private List<string> _languageOptions = [];

    /// <summary>
    /// 语言选择框模式 ComboBox 的显示选项列表。
    /// </summary>
    private List<string> _selectorModeOptions = [];

    /// <summary>
    /// 终端类型 ComboBox 的显示选项列表。
    /// </summary>
    private List<string> _terminalOptions = [];

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
    /// 当前选中的终端类型索引。
    /// </summary>
    private int _selectedTerminalIndex;

    #endregion

    #region 开关后备字段

    /// <summary>
    /// 执行前确认开关状态。
    /// </summary>
    private bool _confirmBeforeExecution;

    /// <summary>
    /// 执行后自动退出开关状态。
    /// </summary>
    private bool _autoExitAfterExecution;

    #endregion

    #region 事件

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

    #endregion

    /// <summary>
    /// 初始化设置 ViewModel 实例，从 <see cref="Config"/> 加载当前配置并构建选项列表。
    /// </summary>
    public SettingsViewModel()
    {
        _isSuppressingChanges = true;
        BuildOptionLists();
        SynchronizeFromConfig();
        _isSuppressingChanges = false;
    }

    #region 选项列表属性

    /// <summary>
    /// 主题风格 ComboBox 的本地化显示选项列表。
    /// </summary>
    /// <value>包含所有 <see cref="ThemeStyle"/> 枚举值的本地化名称。列表引用在语言切换时替换。</value>
    public List<string> ThemeOptions
    {
        get => _themeOptions;
        private set => SetProperty(ref _themeOptions, value);
    }

    /// <summary>
    /// 显示语言 ComboBox 的本地化显示选项列表。
    /// </summary>
    /// <value>包含所有 <see cref="DisplayLanguage"/> 枚举值的本地化名称。</value>
    public List<string> LanguageOptions
    {
        get => _languageOptions;
        private set => SetProperty(ref _languageOptions, value);
    }

    /// <summary>
    /// 语言选择框模式 ComboBox 的本地化显示选项列表。
    /// </summary>
    /// <value>包含所有 <see cref="LanguageSelectorMode"/> 枚举值的本地化名称。</value>
    public List<string> SelectorModeOptions
    {
        get => _selectorModeOptions;
        private set => SetProperty(ref _selectorModeOptions, value);
    }

    /// <summary>
    /// 终端类型 ComboBox 的本地化显示选项列表。
    /// </summary>
    /// <value>包含所有 <see cref="TerminalType"/> 枚举值的本地化名称。</value>
    public List<string> TerminalOptions
    {
        get => _terminalOptions;
        private set => SetProperty(ref _terminalOptions, value);
    }

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
    /// 终端类型 ComboBox 的当前选中索引。
    /// </summary>
    /// <value>对应 <see cref="TerminalType"/> 枚举的整型值。设置时同步写入 Config。</value>
    public int SelectedTerminalIndex
    {
        get => _selectedTerminalIndex;
        set
        {
            if (SetProperty(ref _selectedTerminalIndex, value) && !_isSuppressingChanges && value >= 0)
            {
                Config.Terminal = (TerminalType)value;
            }
        }
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
    /// 执行代码后是否自动退出应用程序。
    /// </summary>
    /// <value>true 表示自动退出，false 表示保持运行。设置时同步写入 Config。</value>
    public bool AutoExitAfterExecution
    {
        get => _autoExitAfterExecution;
        set
        {
            if (SetProperty(ref _autoExitAfterExecution, value) && !_isSuppressingChanges)
            {
                Config.AutoExitAfterExecution = value;
            }
        }
    }

    #endregion

    #region 关于信息属性（只读）

    /// <summary>
    /// 本地化的应用程序显示名称。
    /// </summary>
    /// <value>根据当前语言返回 "一次运行" 或 "RunOnce"。语言切换时需通过 PropertyChanged 通知更新。</value>
    public string AppName => Config.AppName;

    /// <summary>
    /// 应用程序版本号。
    /// </summary>
    /// <value>固定值，格式为 "0.1.0"。</value>
    public string Version => Config.Version;

    /// <summary>
    /// 应用程序作者名称。
    /// </summary>
    /// <value>固定值 "WaterRun"。</value>
    public string Author => Config.Author;

    /// <summary>
    /// 版本与作者的组合显示文本。
    /// </summary>
    /// <value>格式为 "v{Version} · {Author}"，用于宽屏左侧面板。</value>
    public string VersionAndAuthor => $"v{Config.Version} · {Config.Author}";

    /// <summary>
    /// 格式化的编译时间文本。
    /// </summary>
    /// <value>格式为 "yyyy-MM-dd HH:mm:ss"，使用不变文化格式。</value>
    public string BuildTimeText => _buildTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// GitHub 仓库 URL。
    /// </summary>
    /// <value>固定 URL 字符串。</value>
    public string GitHubUrl => Config.GitHubUrl;

    /// <summary>
    /// 微软商店 URL 是否可用。
    /// </summary>
    /// <value>true 表示 URL 非空，可显示商店链接。</value>
    public bool HasStoreUrl => !string.IsNullOrEmpty(Config.MicrosoftStoreUrl);

    /// <summary>
    /// 微软商店 URL。
    /// </summary>
    /// <value>商店链接字符串，暂为空。</value>
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
        Config.TempFilePrefix = "__RunOnceTMP__";
        Config.ConfidenceThreshold = Config.DefaultConfidenceThreshold;
        Config.ResetAllLanguageCommands();

        return ("__RunOnceTMP__", Config.DefaultConfidenceThreshold, Config.GetAllLanguageCommands());
    }

    #endregion

    #region 整体重置

    /// <summary>
    /// 将所有设置重置为默认值并同步 ViewModel 状态。
    /// </summary>
    /// <remarks>
    /// 调用 <see cref="Config.ResetAllSettings"/> 后重建选项列表并同步控件状态。
    /// 调用方需额外处理主题应用与 UI 刷新。
    /// </remarks>
    public void ResetAllSettings()
    {
        Config.ResetAllSettings();

        _isSuppressingChanges = true;
        BuildOptionLists();
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
    /// 重建选项列表（使用新语言的显示名称）、重新同步选中索引、通知 AppName 变更。
    /// 此方法内部使用抑制标志防止 Config 回写。
    /// </remarks>
    public void RefreshAfterLanguageChange()
    {
        _isSuppressingChanges = true;
        BuildOptionLists();
        SynchronizeFromConfig();
        OnPropertyChanged(nameof(AppName));
        _isSuppressingChanges = false;
    }

    #endregion

    #region 私有辅助方法

    /// <summary>
    /// 从 <see cref="Config"/> 的本地化方法构建所有 ComboBox 的选项列表。
    /// </summary>
    private void BuildOptionLists()
    {
        ThemeOptions = Enum.GetValues<ThemeStyle>().Select(Config.GetThemeDisplayName).ToList();
        LanguageOptions = Enum.GetValues<DisplayLanguage>().Select(Config.GetLanguageDisplayName).ToList();
        SelectorModeOptions = Enum.GetValues<LanguageSelectorMode>().Select(Config.GetSelectorModeDisplayName).ToList();
        TerminalOptions = Enum.GetValues<TerminalType>().Select(Config.GetTerminalDisplayName).ToList();
    }

    /// <summary>
    /// 从 <see cref="Config"/> 读取当前值并设置到对应的后备字段与属性。
    /// </summary>
    private void SynchronizeFromConfig()
    {
        SelectedThemeIndex = (int)Config.Theme;
        SelectedLanguageIndex = (int)Config.Language;
        SelectedSelectorModeIndex = (int)Config.SelectorMode;
        SelectedTerminalIndex = (int)Config.Terminal;
        ConfirmBeforeExecution = Config.ConfirmBeforeExecution;
        AutoExitAfterExecution = Config.AutoExitAfterExecution;
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