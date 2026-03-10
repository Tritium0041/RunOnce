/*
 * 代码编辑器页面视图
 * 提供代码编辑、语法高亮、语言检测与脚本执行的 View 层实现
 *
 * @author: WaterRun
 * @file: View/Editor.xaml.cs
 * @date: 2026-03-10
 */

#nullable enable

using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RunOnce.Static;
using RunOnce.ViewModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using TextGetOptions = Microsoft.UI.Text.TextGetOptions;

namespace RunOnce.View;

/// <summary>
/// 代码编辑器页面，提供代码编辑、语法高亮、语言检测与执行功能。
/// </summary>
/// <remarks>
/// 不变量：<see cref="ViewModel"/> 在构造时创建，生命周期与页面一致；
/// 语法高亮通过 80ms 去抖定时器异步应用，不阻塞用户输入；
/// 在鼠标拖动选区期间暂停高亮，防止选区闪烁/丢失；
/// 格式化操作前后保存并恢复 ScrollViewer 滚动位置，防止视口抖动。
/// 线程安全：所有成员必须在 UI 线程访问。
/// 副作用：高亮操作修改 RichEditBox 的字符格式；执行操作创建临时文件并启动终端进程。
/// </remarks>
public sealed partial class Editor : Page
{
    /// <summary>
    /// 自动检测的语言选择标识，区别于实际语言标识符。
    /// </summary>
    private const string AutoDetectToken = "\0auto";

    /// <summary>
    /// 编辑器页面的 ViewModel 实例。
    /// </summary>
    public EditorViewModel ViewModel { get; }

    /// <summary>
    /// 高亮与检测的去抖定时器。
    /// </summary>
    private DispatcherQueueTimer? _updateTimer;

    /// <summary>
    /// 标识当前是否正在应用字符格式，防止 SelectionChanged/TextChanged 回调干扰。
    /// </summary>
    private bool _isApplyingFormatting;

    /// <summary>
    /// 标识当前是否正在执行流程中，防止重入。
    /// </summary>
    private bool _isExecuting;

    /// <summary>
    /// 标识当前是否处于鼠标拖动选区过程。
    /// </summary>
    private bool _isPointerSelecting;

    /// <summary>
    /// 标识是否在拖动结束后补做一次高亮。
    /// </summary>
    private bool _pendingHighlightAfterPointerSelection;

    /// <summary>
    /// RichEditBox 内部 ScrollViewer 的缓存引用，在页面加载时获取。
    /// </summary>
    private ScrollViewer? _scrollViewer;

    /// <summary>
    /// 初始化编辑器页面实例。
    /// </summary>
    public Editor()
    {
        ViewModel = new EditorViewModel();
        InitializeComponent();
        Loaded += OnPageLoaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    /// <summary>
    /// 处理页面加载完成事件。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        EnsureTimerInitialized();
        CacheScrollViewer();
        InitializeWorkingDirectory();
        CodeEditor.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// 处理实际主题变更事件，重新应用高亮以更新颜色。
    /// </summary>
    /// <param name="sender">触发主题变更的元素。</param>
    /// <param name="args">事件参数。</param>
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_isPointerSelecting)
        {
            _pendingHighlightAfterPointerSelection = true;
            return;
        }

        ApplyHighlighting();
    }

    #region 初始化

    /// <summary>
    /// 确保去抖定时器已初始化。
    /// </summary>
    private void EnsureTimerInitialized()
    {
        if (_updateTimer is not null)
        {
            return;
        }

        _updateTimer = DispatcherQueue.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromMilliseconds(80);
        _updateTimer.IsRepeating = false;
        _updateTimer.Tick += (_, _) => PerformDebouncedUpdate();
    }

    /// <summary>
    /// 在 RichEditBox 的可视树中查找并缓存内部 ScrollViewer 引用。
    /// </summary>
    /// <remarks>
    /// 必须在页面 Loaded 后调用，此时可视树已构建完成。
    /// 缓存的 ScrollViewer 用于在格式化操作前后保存和恢复滚动位置。
    /// </remarks>
    private void CacheScrollViewer()
    {
        _scrollViewer = FindDescendant<ScrollViewer>(CodeEditor);
    }

    /// <summary>
    /// 在可视树中递归查找指定类型的第一个后代元素。
    /// </summary>
    /// <typeparam name="T">要查找的元素类型。</typeparam>
    /// <param name="parent">搜索起点的父元素。</param>
    /// <returns>找到的第一个匹配元素；若未找到则返回 null。</returns>
    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target)
            {
                return target;
            }

            T? result = FindDescendant<T>(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// 从启动参数或环境变量初始化工作目录。
    /// </summary>
    private void InitializeWorkingDirectory()
    {
        if (Application.Current is App app && !string.IsNullOrEmpty(app.LaunchArguments))
        {
            string candidate = app.LaunchArguments.Trim('"');
            if (Directory.Exists(candidate))
            {
                ViewModel.WorkingDirectory = candidate;
                return;
            }
        }

        string[] args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            string candidate = args[1].Trim('"');
            if (Directory.Exists(candidate))
            {
                ViewModel.WorkingDirectory = candidate;
            }
        }
    }

    #endregion

    #region 指针拖动选区保护

    /// <summary>
    /// 处理编辑器指针按下事件，进入拖动选区保护状态。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">指针事件参数。</param>
    private void CodeEditor_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(CodeEditor);
        if (point.Properties.IsLeftButtonPressed)
        {
            _isPointerSelecting = true;
            _pendingHighlightAfterPointerSelection = false;
        }
    }

    /// <summary>
    /// 处理编辑器指针释放事件，退出拖动选区保护状态。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">指针事件参数。</param>
    private void CodeEditor_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndPointerSelectionAndFlushHighlight();
    }

    /// <summary>
    /// 处理编辑器指针取消事件，退出拖动选区保护状态。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">指针事件参数。</param>
    private void CodeEditor_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        EndPointerSelectionAndFlushHighlight();
    }

    /// <summary>
    /// 处理编辑器指针捕获丢失事件，退出拖动选区保护状态。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private void CodeEditor_PointerCaptureLost(object sender, RoutedEventArgs e)
    {
        EndPointerSelectionAndFlushHighlight();
    }

    /// <summary>
    /// 处理编辑器失焦事件，退出拖动选区保护状态。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private void CodeEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        EndPointerSelectionAndFlushHighlight();
    }

    /// <summary>
    /// 结束拖动选区状态，并在必要时补做一次高亮。
    /// </summary>
    private void EndPointerSelectionAndFlushHighlight()
    {
        _isPointerSelecting = false;

        if (_pendingHighlightAfterPointerSelection)
        {
            _pendingHighlightAfterPointerSelection = false;
            ApplyHighlighting();
        }
    }

    #endregion

    #region 文本与光标事件

    /// <summary>
    /// 处理代码文本变更事件，启动去抖定时器。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private void CodeEditor_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_isApplyingFormatting)
        {
            return;
        }

        _updateTimer?.Stop();
        _updateTimer?.Start();
    }

    /// <summary>
    /// 处理光标位置变更事件，更新 ViewModel 中的行列信息。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private void CodeEditor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_isApplyingFormatting)
        {
            return;
        }

        string text = GetPlainText();
        int position = CodeEditor.Document.Selection.StartPosition;
        ViewModel.UpdateCursorPosition(text, position);
    }

    /// <summary>
    /// 去抖定时器回调，执行语言检测与语法高亮。
    /// </summary>
    private void PerformDebouncedUpdate()
    {
        string text = GetPlainText();
        ViewModel.RunDetection(NormalizeForAnalysis(text));

        if (_isPointerSelecting)
        {
            _pendingHighlightAfterPointerSelection = true;
            return;
        }

        ApplyHighlighting(text);
    }

    #endregion

    #region 键盘处理

    /// <summary>
    /// 处理编辑器的按键预览事件，拦截 Ctrl+Enter 和 Tab 键。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">按键路由事件参数。</param>
    /// <remarks>
    /// PreviewKeyDown 在 RichEditBox 自身处理按键之前触发，
    /// 可以可靠拦截 Ctrl+Enter（阻止 RichEditBox 将其解释为段落换行）
    /// 以及 Tab（实现自定义缩进功能）。
    /// </remarks>
    private void CodeEditor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CoreVirtualKeyStates ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            if ((ctrlState & CoreVirtualKeyStates.Down) != 0)
            {
                e.Handled = true;
                HandleExecuteRequest();
                return;
            }
        }

        if (e.Key == VirtualKey.Tab)
        {
            e.Handled = true;

            CoreVirtualKeyStates shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            bool isShift = (shiftState & CoreVirtualKeyStates.Down) != 0;

            if (isShift)
            {
                RemoveLeadingIndent();
            }
            else
            {
                CodeEditor.Document.Selection.TypeText("    ");
            }
        }
    }

    /// <summary>
    /// 处理 Ctrl+Enter 快捷键，触发代码执行（Page 级后备，当 RichEditBox 未聚焦时生效）。
    /// </summary>
    /// <param name="sender">快捷键加速器对象。</param>
    /// <param name="args">快捷键调用事件参数。</param>
    private void ExecuteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        HandleExecuteRequest();
    }

    /// <summary>
    /// 删除当前行开头的至多 4 个空格。
    /// </summary>
    private void RemoveLeadingIndent()
    {
        string text = GetPlainText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        int cursorPos = CodeEditor.Document.Selection.StartPosition;
        int lineStart = cursorPos;
        while (lineStart > 0 && text[lineStart - 1] != '\r')
        {
            lineStart--;
        }

        int spacesToRemove = 0;
        for (int i = lineStart; i < text.Length && i < lineStart + 4 && text[i] == ' '; i++)
        {
            spacesToRemove++;
        }

        if (spacesToRemove > 0)
        {
            var range = CodeEditor.Document.GetRange(lineStart, lineStart + spacesToRemove);
            range.Text = string.Empty;
        }
    }

    #endregion

    #region 语法高亮

    /// <summary>
    /// 对编辑器内容应用语法高亮着色。
    /// </summary>
    /// <param name="text">编辑器原始文本，若为 null 则从编辑器获取。</param>
    /// <remarks>
    /// 在鼠标拖动选区期间暂停高亮应用，避免拖动选区闪烁或丢失。
    /// 格式化操作前保存 ScrollViewer 的滚动位置和文本选区，
    /// 操作完成后恢复，防止视口抖动。
    /// </remarks>
    private void ApplyHighlighting(string? text = null)
    {
        if (_isPointerSelecting)
        {
            _pendingHighlightAfterPointerSelection = true;
            return;
        }

        text ??= GetPlainText();
        string language = ViewModel.EffectiveLanguage;

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        double? savedVerticalOffset = _scrollViewer?.VerticalOffset;
        double? savedHorizontalOffset = _scrollViewer?.HorizontalOffset;

        _isApplyingFormatting = true;

        try
        {
            var doc = CodeEditor.Document;

            int selStart = doc.Selection.StartPosition;
            int selEnd = doc.Selection.EndPosition;

            var fullRange = doc.GetRange(0, text.Length);
            fullRange.CharacterFormat.ForegroundColor = GetDefaultTextColor();

            if (!string.IsNullOrEmpty(language))
            {
                string normalizedText = NormalizeForAnalysis(text);
                IReadOnlyList<HighlightSpan> spans = Highlight.Analyze(normalizedText, language);

                foreach (HighlightSpan span in spans)
                {
                    if (span.Start >= 0 && span.End <= text.Length)
                    {
                        var range = doc.GetRange(span.Start, span.End);
                        range.CharacterFormat.ForegroundColor = GetColorForToken(span.Type);
                    }
                }
            }

            doc.Selection.SetRange(selStart, selEnd);
        }
        finally
        {
            _isApplyingFormatting = false;
        }

        if (savedVerticalOffset.HasValue && savedHorizontalOffset.HasValue)
        {
            _scrollViewer?.ChangeView(savedHorizontalOffset.Value, savedVerticalOffset.Value, null, disableAnimation: true);
        }
    }

    /// <summary>
    /// 获取当前主题下的默认文本颜色。
    /// </summary>
    /// <returns>默认前景色。</returns>
    private Color GetDefaultTextColor()
    {
        return ActualTheme == ElementTheme.Dark
            ? Color.FromArgb(255, 204, 204, 204)
            : Color.FromArgb(255, 30, 30, 30);
    }

    /// <summary>
    /// 获取指定 Token 类型在当前主题下的着色。
    /// </summary>
    /// <param name="type">高亮 Token 类型。</param>
    /// <returns>对应的前景色。</returns>
    private Color GetColorForToken(TokenType type)
    {
        bool dark = ActualTheme == ElementTheme.Dark;
        return type switch
        {
            TokenType.Keyword => dark ? Color.FromArgb(255, 86, 156, 214) : Color.FromArgb(255, 0, 0, 255),
            TokenType.String => dark ? Color.FromArgb(255, 206, 145, 120) : Color.FromArgb(255, 163, 21, 21),
            TokenType.Comment => dark ? Color.FromArgb(255, 106, 153, 85) : Color.FromArgb(255, 0, 128, 0),
            TokenType.Number => dark ? Color.FromArgb(255, 181, 206, 168) : Color.FromArgb(255, 9, 134, 88),
            _ => GetDefaultTextColor(),
        };
    }

    #endregion

    #region 执行流程

    /// <summary>
    /// 处理执行请求，包含语言选择、确认对话框与实际执行。由 MainWindow 的运行按钮和 Ctrl+Enter 调用。
    /// </summary>
    public async void HandleExecuteRequest()
    {
        if (_isExecuting || XamlRoot is null)
        {
            return;
        }

        string code = GetPlainText();
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        _isExecuting = true;

        try
        {
            string? language = await DetermineExecutionLanguageAsync();
            if (string.IsNullOrEmpty(language))
            {
                return;
            }

            if (Config.ConfirmBeforeExecution)
            {
                ContentDialog confirmDialog = new()
                {
                    Title = Text.Localize("执行"),
                    Content = Text.Localize("确定要执行此代码吗？"),
                    PrimaryButtonText = Text.Localize("执行"),
                    CloseButtonText = Text.Localize("取消"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot,
                };

                // Backspace 键取消确认对话框
                confirmDialog.KeyDown += (_, args) =>
                {
                    if (args.Key == VirtualKey.Back)
                    {
                        args.Handled = true;
                        confirmDialog.Hide();
                    }
                };

                if (await confirmDialog.ShowAsync() is not ContentDialogResult.Primary)
                {
                    return;
                }
            }

            try
            {
                ViewModel.Execute(code, language);
            }
            catch (Exception ex)
            {
                ContentDialog errorDialog = new()
                {
                    Title = Text.Localize("执行"),
                    Content = ex.Message,
                    CloseButtonText = Text.Localize("确定"),
                    XamlRoot = XamlRoot,
                };
                await errorDialog.ShowAsync();
                return;
            }

            if (Config.AutoExitOnExecution)
            {
                Application.Current.Exit();
            }
        }
        finally
        {
            _isExecuting = false;
        }
    }

    /// <summary>
    /// 根据配置确定执行使用的语言，必要时弹出语言选择对话框。
    /// </summary>
    /// <returns>选定的语言标识符；若用户取消则返回 null。</returns>
    private async Task<string?> DetermineExecutionLanguageAsync()
    {
        if (ViewModel.ShouldShowLanguageSelector)
        {
            return await ShowLanguageSelectionDialogAsync(includeAutoDetect: false);
        }

        string language = ViewModel.EffectiveLanguage;
        if (!string.IsNullOrEmpty(language))
        {
            return language;
        }

        return await ShowLanguageSelectionDialogAsync(includeAutoDetect: false);
    }

    #endregion

    #region 语言选择对话框

    /// <summary>
    /// 处理状态栏语言指示器按钮点击事件，弹出语言选择对话框供用户手动切换。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private async void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null)
        {
            return;
        }

        string? selected = await ShowLanguageSelectionDialogAsync(includeAutoDetect: true);
        if (selected is null)
        {
            return;
        }

        if (selected == AutoDetectToken)
        {
            ViewModel.ManualLanguage = null;
        }
        else
        {
            ViewModel.ManualLanguage = selected;
        }

        ApplyHighlighting();
    }

    /// <summary>
    /// 显示语言选择对话框。
    /// </summary>
    /// <param name="includeAutoDetect">是否包含"自动检测"选项。</param>
    /// <returns>
    /// 用户选定的语言标识符；选择"自动检测"时返回 <see cref="AutoDetectToken"/>；
    /// 用户取消时返回 null。
    /// </returns>
    /// <remarks>
    /// 支持键盘操作：Enter 键确认当前选中项；Backspace 键取消并关闭对话框。
    /// ListView 的 PreviewKeyDown 事件在列表自身处理前触发（隧道路由），
    /// 可可靠拦截 Enter 键并通过标志位区分 Enter 确认与按钮点击取消。
    /// </remarks>
    private async Task<string?> ShowLanguageSelectionDialogAsync(bool includeAutoDetect)
    {
        IReadOnlyList<DetectionResult> results = ViewModel.DetectionResults;

        List<(string Language, double Confidence)> sortedItems = results
            .Where(r => r.Confidence > 0)
            .OrderByDescending(r => r.Confidence)
            .Select(r => (r.Language, r.Confidence))
            .ToList();

        foreach (string lang in Config.SupportedLanguages)
        {
            if (sortedItems.All(i => !string.Equals(i.Language, lang, StringComparison.OrdinalIgnoreCase)))
            {
                sortedItems.Add((lang, 0));
            }
        }

        List<string> languageMap = [];
        ListView listView = new()
        {
            SelectionMode = ListViewSelectionMode.Single,
            MinWidth = 350,
        };

        if (includeAutoDetect)
        {
            languageMap.Add(AutoDetectToken);
            Grid autoGrid = new() { Padding = new Thickness(2, 6, 2, 6) };
            autoGrid.Children.Add(new TextBlock
            {
                Text = Text.Localize("自动检测"),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
            });
            listView.Items.Add(autoGrid);
        }

        foreach ((string language, double confidence) in sortedItems)
        {
            languageMap.Add(language);

            Grid itemGrid = new() { Padding = new Thickness(2, 6, 2, 6) };
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock nameBlock = new()
            {
                Text = language.ToUpperInvariant(),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(nameBlock, 0);
            itemGrid.Children.Add(nameBlock);

            if (confidence > 0)
            {
                TextBlock confBlock = new()
                {
                    Text = confidence.ToString("P0"),
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(16, 0, 0, 0),
                };
                Grid.SetColumn(confBlock, 1);
                itemGrid.Children.Add(confBlock);
            }

            listView.Items.Add(itemGrid);
        }

        int preSelectIndex = 0;
        if (includeAutoDetect && ViewModel.ManualLanguage is not null)
        {
            int langIndex = languageMap.IndexOf(ViewModel.ManualLanguage);
            if (langIndex >= 0)
            {
                preSelectIndex = langIndex;
            }
        }

        listView.SelectedIndex = preSelectIndex;

        ContentDialog dialog = new()
        {
            Title = Text.Localize("选择语言"),
            Content = listView,
            PrimaryButtonText = Text.Localize("确定"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        // Enter 键在 ListView 中直接确认：PreviewKeyDown 于列表内部处理前触发（隧道路由），
        // 可靠拦截 Enter 键，通过标志位区分 Enter 确认与按钮关闭两种路径。
        bool confirmedViaEnter = false;
        listView.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                confirmedViaEnter = true;
                dialog.Hide();
            }
        };

        // Backspace 键取消语言选择对话框
        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Back)
            {
                args.Handled = true;
                dialog.Hide();
            }
        };

        ContentDialogResult result = await dialog.ShowAsync();

        if (result is ContentDialogResult.Primary || confirmedViaEnter)
        {
            int selectedIndex = listView.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < languageMap.Count)
            {
                return languageMap[selectedIndex];
            }
        }

        return null;
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 从 RichEditBox 获取纯文本内容，去除尾部自动追加的 \r。
    /// </summary>
    /// <returns>编辑器中的纯文本（\r 作为换行符）。</returns>
    private string GetPlainText()
    {
        CodeEditor.Document.GetText(TextGetOptions.None, out string text);

        if (text.Length > 0 && text[^1] == '\r')
        {
            text = text[..^1];
        }

        return text;
    }

    /// <summary>
    /// 将 RichEditBox 文本规范化为 \n 换行，供 Highlight.Analyze 和 LanguageDetector 使用。
    /// </summary>
    /// <param name="text">原始文本（\r 换行）。</param>
    /// <returns>规范化后的文本（\n 换行）。</returns>
    private static string NormalizeForAnalysis(string text)
    {
        return text.Replace('\r', '\n');
    }

    /// <summary>
    /// 刷新本地化文本，供 MainWindow 在语言切换后调用。
    /// </summary>
    public void RefreshLocalizedTexts()
    {
        ViewModel.RefreshLocalizedTexts();
    }

    #endregion
}