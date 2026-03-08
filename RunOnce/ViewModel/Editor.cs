/*
 * 代码编辑器页面 ViewModel
 * 管理编辑器页面的光标位置、语言检测结果与执行逻辑
 *
 * @author: WaterRun
 * @file: ViewModel/Editor.cs
 * @date: 2026-03-08
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
/// 代码编辑器页面的 ViewModel，承载光标位置、语言检测结果与执行状态。
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
    /// 属性值变更时触发的事件。
    /// </summary>
    /// <remarks>
    /// 触发时机：调用 <see cref="SetProperty{T}"/> 且新旧值不相等时，或显式调用 <see cref="OnPropertyChanged"/>。
    /// 线程上下文：在调用线程触发，通常为 UI 线程。
    /// 订阅/取消订阅：无特殊注意事项。
    /// </remarks>
    public event PropertyChangedEventHandler? PropertyChanged;

    #region 光标位置属性

    /// <summary>
    /// 获取当前光标所在行号（从 1 开始）。
    /// </summary>
    /// <value>非负整数，默认为 1，只读对外。</value>
    public int CurrentLine
    {
        get => _currentLine;
        private set => SetProperty(ref _currentLine, value);
    }

    /// <summary>
    /// 获取当前光标所在列号（从 1 开始）。
    /// </summary>
    /// <value>非负整数，默认为 1，只读对外。</value>
    public int CurrentColumn
    {
        get => _currentColumn;
        private set => SetProperty(ref _currentColumn, value);
    }

    /// <summary>
    /// 获取本地化的光标位置显示文本。
    /// </summary>
    /// <value>格式为 "Ln {行}, Col {列}" 或对应中文，根据当前语言设置决定。</value>
    public string PositionDisplay => $"{Text.Localize("行")} {_currentLine}, {Text.Localize("列")} {_currentColumn}";

    #endregion

    #region 语言检测属性

    /// <summary>
    /// 获取语言检测结果的本地化显示文本。
    /// </summary>
    /// <value>
    /// 手动指定时显示语言名称大写；自动检测到时显示 "LANGUAGE (xx%)"；
    /// 未检测到时显示 "纯文本" / "Plain Text"。
    /// </value>
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
    /// <value>优先返回手动指定的语言，若未手动指定则返回自动检测结果。</value>
    public string EffectiveLanguage => !string.IsNullOrEmpty(_manualLanguage) ? _manualLanguage : _detectedLanguage;

    /// <summary>
    /// 获取自动检测的最高置信度。
    /// </summary>
    /// <value>范围 [0.0, 1.0]，0 表示未检测到。</value>
    public double DetectedConfidence => _detectedConfidence;

    /// <summary>
    /// 获取所有语言的检测结果列表。
    /// </summary>
    /// <value>按置信度降序排列的结果列表，不为 null。</value>
    public IReadOnlyList<DetectionResult> DetectionResults => _detectionResults;

    /// <summary>
    /// 获取当前检测结果是否达到可信标准。
    /// </summary>
    /// <value>true 表示置信度大于等于 <see cref="Config.ConfidenceThreshold"/>。</value>
    public bool IsConfident => _detectedConfidence >= Config.ConfidenceThreshold;

    /// <summary>
    /// 根据配置判断执行前是否应显示语言选择框。
    /// </summary>
    /// <value>
    /// AlwaysShow 模式下始终为 true；AutoHide 模式下当不可信或语言为空时为 true。
    /// </value>
    public bool ShouldShowLanguageSelector => Config.SelectorMode switch
    {
        LanguageSelectorMode.AlwaysShow => true,
        LanguageSelectorMode.AutoHide => !IsConfident || string.IsNullOrEmpty(EffectiveLanguage),
        _ => true,
    };

    /// <summary>
    /// 获取或设置用户手动指定的语言标识符。
    /// </summary>
    /// <value>为 null 表示使用自动检测结果；非 null 时必须是有效的语言标识符。</value>
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

    #region 工作目录

    /// <summary>
    /// 获取或设置脚本执行的工作目录。
    /// </summary>
    /// <value>默认为当前进程的工作目录，不为 null。</value>
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
    /// 对代码执行语言检测并更新所有相关属性。
    /// </summary>
    /// <param name="code">待检测的代码文本，允许为 null 或空。</param>
    public void RunDetection(string code)
    {
        _detectionResults = LanguageDetector.Detect(code);
        DetectionResult top = _detectionResults.FirstOrDefault();

        if (top.Confidence > 0)
        {
            _detectedLanguage = top.Language;
            _detectedConfidence = top.Confidence;
        }
        else
        {
            _detectedLanguage = string.Empty;
            _detectedConfidence = 0;
        }

        OnPropertyChanged(nameof(DetectedLanguageDisplay));
        OnPropertyChanged(nameof(DetectionResults));
        OnPropertyChanged(nameof(EffectiveLanguage));
        OnPropertyChanged(nameof(IsConfident));
        OnPropertyChanged(nameof(ShouldShowLanguageSelector));
        OnPropertyChanged(nameof(DetectedConfidence));
    }

    /// <summary>
    /// 执行代码脚本。
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

        Exec.Execute(normalizedCode, language, WorkingDirectory);
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

    #region INotifyPropertyChanged 实现

    /// <summary>
    /// 触发指定属性的变更通知。
    /// </summary>
    /// <param name="propertyName">变更的属性名称。</param>
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
}
