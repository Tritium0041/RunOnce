/*
 * 代码编辑器页面 ViewModel
 * 管理编辑器页面的光标位置、语言检测结果、命令行参数与执行逻辑
 *
 * @author: WaterRun
 * @file: ViewModel/Editor.cs
 * @date: 2026-03-19
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using RunOnce.Static;

namespace RunOnce.ViewModel;

/// <summary>
/// 代码编辑器页面的 ViewModel，承载光标位置、语言检测结果、命令行参数与执行状态。
/// </summary>
/// <remarks>
/// 不变量：代码文本由 View 层的 RichEditBox 管理，本类仅持有检测与光标等派生状态。
/// 线程安全：非线程安全，所有成员必须在 UI 线程访问。
/// 副作用：<see cref="Execute"/> 会创建临时文件并启动外部终端进程。
/// </remarks>
public sealed class EditorViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// 当前光标所在行号。
    /// </summary>
    private int _currentLine = 1;

    /// <summary>
    /// 当前光标所在列号。
    /// </summary>
    private int _currentColumn = 1;

    /// <summary>
    /// 自动检测到的语言标识符。
    /// </summary>
    private string _detectedLanguage = string.Empty;

    /// <summary>
    /// 自动检测到的最高置信度。
    /// </summary>
    private double _detectedConfidence;

    /// <summary>
    /// 所有语言的检测结果列表。
    /// </summary>
    private IReadOnlyList<DetectionResult> _detectionResults = [];

    /// <summary>
    /// 用户手动指定的语言标识符，为 null 表示使用自动检测结果。
    /// </summary>
    private string? _manualLanguage;

    /// <summary>
    /// 用户输入的命令行参数，仅在内存中保持，不持久化存储。
    /// </summary>
    private string _commandLineArguments = string.Empty;

    /// <summary>
    /// 属性值变更时触发的事件。
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    #region 光标位置属性

    /// <summary>
    /// 获取当前光标所在行号（从 1 开始）。
    /// </summary>
    public int CurrentLine
    {
        get => _currentLine;
        private set => SetProperty(ref _currentLine, value);
    }

    /// <summary>
    /// 获取当前光标所在列号（从 1 开始）。
    /// </summary>
    public int CurrentColumn
    {
        get => _currentColumn;
        private set => SetProperty(ref _currentColumn, value);
    }

    /// <summary>
    /// 获取本地化的光标位置显示文本。
    /// </summary>
    public string PositionDisplay => $"{Text.Localize("行")} {_currentLine}, {Text.Localize("列")} {_currentColumn}";

    #endregion

    #region 语言检测属性

    /// <summary>
    /// 获取语言检测结果的本地化显示文本。
    /// </summary>
    public string DetectedLanguageDisplay
    {
        get
        {
            if (!string.IsNullOrEmpty(_manualLanguage))
            {
                return _manualLanguage.ToUpperInvariant();
            }

            if (string.IsNullOrEmpty(_detectedLanguage) || _detectedConfidence <= 0)
            {
                return Text.Localize("纯文本");
            }

            return $"{_detectedLanguage.ToUpperInvariant()} ({_detectedConfidence:P0})";
        }
    }

    /// <summary>
    /// 获取当前生效的语言标识符。
    /// </summary>
    public string EffectiveLanguage => !string.IsNullOrEmpty(_manualLanguage) ? _manualLanguage : _detectedLanguage;

    /// <summary>
    /// 获取自动检测的最高置信度。
    /// </summary>
    public double DetectedConfidence => _detectedConfidence;

    /// <summary>
    /// 获取所有语言的检测结果列表。
    /// </summary>
    public IReadOnlyList<DetectionResult> DetectionResults => _detectionResults;

    /// <summary>
    /// 获取当前检测结果是否达到可信标准。
    /// </summary>
    public bool IsConfident => _detectedConfidence >= Config.ConfidenceThreshold;

    /// <summary>
    /// 根据配置判断执行前是否应显示语言选择框。
    /// </summary>
    public bool ShouldShowLanguageSelector => Config.SelectorMode switch
    {
        LanguageSelectorMode.AlwaysShow => true,
        LanguageSelectorMode.AutoHide => !IsConfident || string.IsNullOrEmpty(EffectiveLanguage),
        _ => true,
    };

    /// <summary>
    /// 获取或设置用户手动指定的语言标识符。
    /// </summary>
    public string? ManualLanguage
    {
        get => _manualLanguage;
        set
        {
            if (SetProperty(ref _manualLanguage, value))
            {
                OnPropertyChanged(nameof(DetectedLanguageDisplay));
                OnPropertyChanged(nameof(EffectiveLanguage));
            }
        }
    }

    #endregion

    #region 命令行参数

    /// <summary>
    /// 获取或设置传递给脚本的命令行参数。
    /// </summary>
    public string CommandLineArguments
    {
        get => _commandLineArguments;
        set
        {
            if (SetProperty(ref _commandLineArguments, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasCommandLineArguments));
            }
        }
    }

    /// <summary>
    /// 获取当前是否已设置非空的命令行参数。
    /// </summary>
    public bool HasCommandLineArguments => !string.IsNullOrWhiteSpace(_commandLineArguments);

    #endregion

    #region 工作目录

    /// <summary>
    /// 获取或设置脚本执行的工作目录。
    /// </summary>
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;

    #endregion

    #region 公开方法

    /// <summary>
    /// 根据文本和字符偏移量更新光标行列信息。
    /// </summary>
    /// <param name="text">RichEditBox 中的原始文本（\r 作为换行符），允许为 null 或空。</param>
    /// <param name="charIndex">光标的字符偏移量，允许为负数（将视为无效并重置为初始位置）。</param>
    public void UpdateCursorPosition(string text, int charIndex)
    {
        if (string.IsNullOrEmpty(text) || charIndex < 0)
        {
            CurrentLine = 1;
            CurrentColumn = 1;
            OnPropertyChanged(nameof(PositionDisplay));
            return;
        }

        int line = 1;
        int column = 1;
        int safeIndex = Math.Min(charIndex, text.Length);

        for (int i = 0; i < safeIndex; i++)
        {
            if (text[i] == '\r')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        CurrentLine = line;
        CurrentColumn = column;
        OnPropertyChanged(nameof(PositionDisplay));
    }

    /// <summary>
    /// 对代码执行渐进式语言检测并更新所有相关属性。
    /// </summary>
    /// <param name="code">待检测的代码文本（已规范化为 \n 换行），允许为 null 或空。</param>
    /// <remarks>
    /// 渐进式策略：从前 N 个字符开始检测，若置信度超过阈值则提前停止，
    /// 否则逐步扩大分析范围直至达到最大字符数限制。
    /// 参数 N 由 <see cref="Config.DetectionInitialChars"/>、
    /// <see cref="Config.DetectionIncrementChars"/> 和 <see cref="Config.DetectionMaxChars"/> 控制。
    /// </remarks>
    public void RunDetection(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            _detectionResults = Config.SupportedLanguages
                .Select(lang => new DetectionResult(lang, 0.0))
                .ToList();
            _detectedLanguage = string.Empty;
            _detectedConfidence = 0;
            NotifyDetectionPropertiesChanged();
            return;
        }

        int initialChars = Config.DetectionInitialChars;
        int incrementChars = Config.DetectionIncrementChars;
        int maxChars = Config.DetectionMaxChars;
        double threshold = Config.ConfidenceThreshold;

        int analyzeLength = Math.Min(initialChars, code.Length);
        IReadOnlyList<DetectionResult> lastResults = [];
        DetectionResult lastTop = default;

        while (analyzeLength <= code.Length)
        {
            string snippet = code[..analyzeLength];
            lastResults = LanguageDetector.Detect(snippet);
            lastTop = lastResults.FirstOrDefault();

            if (lastTop.Confidence >= threshold)
            {
                break;
            }

            if (analyzeLength >= Math.Min(maxChars, code.Length))
            {
                break;
            }

            analyzeLength = Math.Min(analyzeLength + incrementChars, Math.Min(maxChars, code.Length));
        }

        _detectionResults = lastResults;

        if (lastTop.Confidence > 0)
        {
            _detectedLanguage = lastTop.Language;
            _detectedConfidence = lastTop.Confidence;
        }
        else
        {
            _detectedLanguage = string.Empty;
            _detectedConfidence = 0;
        }

        NotifyDetectionPropertiesChanged();
    }

    /// <summary>
    /// 执行代码脚本，附带可选的命令行参数。
    /// </summary>
    /// <param name="code">待执行的代码文本，不允许为 null。</param>
    /// <param name="language">目标语言标识符，不允许为 null 或空字符串。</param>
    /// <exception cref="ArgumentNullException">当 code 或 language 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当参数为空白字符串或语言不在支持列表中时抛出。</exception>
    /// <exception cref="IOException">当临时文件创建失败时抛出。</exception>
    /// <exception cref="InvalidOperationException">当终端启动失败时抛出。</exception>
    public void Execute(string code, string language)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(language);

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrEmpty(language))
        {
            return;
        }

        string normalizedCode = code
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\n", "\r\n");

        string? arguments = string.IsNullOrWhiteSpace(_commandLineArguments) ? null : _commandLineArguments;

        Exec.Execute(normalizedCode, language, WorkingDirectory, arguments);
    }

    /// <summary>
    /// 刷新所有本地化相关的属性通知。
    /// </summary>
    public void RefreshLocalizedTexts()
    {
        OnPropertyChanged(nameof(DetectedLanguageDisplay));
        OnPropertyChanged(nameof(PositionDisplay));
    }

    #endregion

    #region 私有辅助方法

    /// <summary>
    /// 触发所有检测相关属性的变更通知。
    /// </summary>
    private void NotifyDetectionPropertiesChanged()
    {
        OnPropertyChanged(nameof(DetectedLanguageDisplay));
        OnPropertyChanged(nameof(DetectionResults));
        OnPropertyChanged(nameof(EffectiveLanguage));
        OnPropertyChanged(nameof(IsConfident));
        OnPropertyChanged(nameof(ShouldShowLanguageSelector));
        OnPropertyChanged(nameof(DetectedConfidence));
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
}