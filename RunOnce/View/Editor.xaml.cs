/*
 * 代码编辑器页面视图
 * 提供代码编辑、语法高亮、语言检测、自定义右键菜单、命令行参数与脚本执行的 View 层实现
 *
 * @author: WaterRun
 * @file: View/Editor.xaml.cs
 * @date: 2026-03-11
 */

#nullable enable

using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RunOnce.Static;
using RunOnce.ViewModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using TextGetOptions = Microsoft.UI.Text.TextGetOptions;

namespace RunOnce.View;

/// <summary>
/// 代码编辑器页面，提供代码编辑、语法高亮、语言检测、自定义右键菜单、命令行参数与执行功能。
/// </summary>
/// <remarks>
/// 不变量：<see cref="ViewModel"/> 在构造时创建，生命周期与页面一致；
/// 语法高亮通过 80ms 去抖定时器异步应用，不阻塞用户输入；
/// 在鼠标拖动选区期间暂停高亮，防止选区闪烁/丢失；
/// 格式化操作前后保存并恢复 ScrollViewer 滚动位置，防止视口抖动；
/// 右键菜单已替换为仅包含代码编辑操作的自定义菜单，空编辑器下右键直接粘贴。
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
    /// 长度限制。
    /// </summary>
    private const int MaxCodeLength = 20480;

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
    /// 标识当前是否展现限制对话框。
    /// </summary>
    private bool _isShowingLimitDialog;

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
        RegisterContextRequestedHandler();
        InitializeWorkingDirectory();
        UpdatePlaceholderText();
        UpdatePlaceholderVisibility();
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
    /// 注册 ContextRequested 事件处理程序以替换默认右键菜单。
    /// </summary>
    /// <remarks>
    /// 使用 AddHandler 并设置 handledEventsToo 为 true，确保即使 RichEditBox
    /// 内部已处理该事件，我们的处理程序仍然运行，从而完全接管右键菜单行为。
    /// </remarks>
    private void RegisterContextRequestedHandler()
    {
        CodeEditor.AddHandler(
            UIElement.ContextRequestedEvent,
            new TypedEventHandler<UIElement, ContextRequestedEventArgs>(CodeEditor_ContextRequested),
            true);
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

    #region 自定义右键菜单

    /// <summary>
    /// 处理编辑器的右键菜单请求事件，替换默认的富文本格式菜单。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="args">上下文请求事件参数。</param>
    /// <remarks>
    /// 空编辑器下直接粘贴剪贴板文本内容，非空编辑器下显示仅包含代码编辑操作的自定义菜单。
    /// </remarks>
    private async void CodeEditor_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        args.Handled = true;

        string text = GetPlainText();
        if (string.IsNullOrEmpty(text))
        {
            await PasteTextFromClipboardAsync();
            return;
        }

        ShowCodeContextMenu(args);
    }

    /// <summary>
    /// 从系统剪贴板获取纯文本并粘贴到编辑器中。
    /// </summary>
    /// <remarks>
    /// 仅在剪贴板包含文本内容时执行粘贴，否则静默忽略。
    /// 粘贴后光标位于文本末尾。
    /// </remarks>
    private async Task PasteTextFromClipboardAsync()
    {
        try
        {
            DataPackageView content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                string text = await content.GetTextAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    string currentText = GetPlainText();
                    int remaining = MaxCodeLength - currentText.Length;
                    if (remaining <= 0)
                    {
                        _ = ShowCharacterLimitDialogAsync();
                        return;
                    }

                    bool truncated = false;
                    if (text.Length > remaining)
                    {
                        text = text[..remaining];
                        truncated = true;
                    }

                    CodeEditor.Document.Selection.TypeText(text);

                    if (truncated)
                    {
                        _ = ShowCharacterLimitDialogAsync();
                    }
                }
            }
        }
        catch
        {
            // CLIP-001: 剪贴板访问失败时静默忽略，不影响编辑器正常使用
        }
    }

    /// <summary>
    /// 构建并显示自定义代码编辑右键菜单。
    /// </summary>
    /// <param name="args">上下文请求事件参数，用于获取菜单弹出位置。</param>
    /// <remarks>
    /// 菜单仅包含代码编辑相关操作：撤销、重做、剪切、复制、粘贴、全选。
    /// 剪切和复制在无文本选区时自动禁用。
    /// </remarks>
    private void ShowCodeContextMenu(ContextRequestedEventArgs args)
    {
        bool hasSelection = CodeEditor.Document.Selection.StartPosition != CodeEditor.Document.Selection.EndPosition;

        MenuFlyout flyout = new();

        //MenuFlyoutItem undoItem = new()
        //{
        //    Text = Text.Localize("撤销"),
        //    Icon = new SymbolIcon(Symbol.Undo),
        //    KeyboardAcceleratorTextOverride = "Ctrl+Z",
        //};
        //undoItem.Click += (_, _) => CodeEditor.Document.Undo();
        //flyout.Items.Add(undoItem);

        MenuFlyoutItem redoItem = new()
        {
            Text = Text.Localize("重做"),
            Icon = new SymbolIcon(Symbol.Redo),
            KeyboardAcceleratorTextOverride = "Ctrl+Y",
        };
        redoItem.Click += (_, _) => CodeEditor.Document.Redo();
        flyout.Items.Add(redoItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutItem cutItem = new()
        {
            Text = Text.Localize("剪切"),
            Icon = new SymbolIcon(Symbol.Cut),
            KeyboardAcceleratorTextOverride = "Ctrl+X",
            IsEnabled = hasSelection,
        };
        cutItem.Click += (_, _) => CodeEditor.Document.Selection.Cut();
        flyout.Items.Add(cutItem);

        MenuFlyoutItem copyItem = new()
        {
            Text = Text.Localize("复制"),
            Icon = new SymbolIcon(Symbol.Copy),
            KeyboardAcceleratorTextOverride = "Ctrl+C",
            IsEnabled = hasSelection,
        };
        copyItem.Click += (_, _) => CodeEditor.Document.Selection.Copy();
        flyout.Items.Add(copyItem);

        MenuFlyoutItem pasteItem = new()
        {
            Text = Text.Localize("粘贴"),
            Icon = new SymbolIcon(Symbol.Paste),
            KeyboardAcceleratorTextOverride = "Ctrl+V",
        };
        pasteItem.Click += (_, _) => CodeEditor.Document.Selection.Paste(0);
        flyout.Items.Add(pasteItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutItem selectAllItem = new()
        {
            Text = Text.Localize("全选"),
            KeyboardAcceleratorTextOverride = "Ctrl+A",
        };
        selectAllItem.Click += (_, _) =>
        {
            string t = GetPlainText();
            CodeEditor.Document.Selection.SetRange(0, t.Length);
        };
        flyout.Items.Add(selectAllItem);

        if (args.TryGetPosition(CodeEditor, out Windows.Foundation.Point point))
        {
            flyout.ShowAt(CodeEditor, new FlyoutShowOptions { Position = point });
        }
        else
        {
            flyout.ShowAt(CodeEditor);
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

        UpdatePlaceholderVisibility();

        string text = GetPlainText();
        if (text.Length > MaxCodeLength)
        {
            EnforceCharacterLimit(text);
        }

        _updateTimer?.Stop();
        _updateTimer?.Start();
    }

    /// <summary>
    /// 字符数量限制。
    /// </summary>
    /// <param name="text">源字符串。</param>
    private void EnforceCharacterLimit(string text)
    {
        _isApplyingFormatting = true;
        try
        {
            var range = CodeEditor.Document.GetRange(MaxCodeLength, text.Length);
            range.Text = string.Empty;
            CodeEditor.Document.Selection.SetRange(MaxCodeLength, MaxCodeLength);
        }
        finally
        {
            _isApplyingFormatting = false;
        }

        _ = ShowCharacterLimitDialogAsync();
    }

    /// <summary>
    /// 显示字符数限制对话框。
    /// </summary>
    private async Task ShowCharacterLimitDialogAsync()
    {
        if (_isShowingLimitDialog || XamlRoot is null)
        {
            return;
        }

        _isShowingLimitDialog = true;
        try
        {
            ContentDialog dialog = new()
            {
                Title = Text.Localize("超出输入限制"),
                Content = Text.Localize("一次运行面向的是\"一次性\"的脚本. 你的脚本已经长到超出这个范围了."),
                CloseButtonText = Text.Localize("确定"),
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }
        finally
        {
            _isShowingLimitDialog = false;
        }
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
    /// 处理编辑器的按键预览事件，拦截 Ctrl+Enter、Ctrl+E、Ctrl+B/I/U 和 Tab 键。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">按键路由事件参数。</param>
    /// <remarks>
    /// PreviewKeyDown 在 RichEditBox 自身处理按键之前触发，
    /// 可以可靠拦截 Ctrl+Enter（阻止 RichEditBox 将其解释为段落换行）、
    /// Ctrl+E（打开命令行参数对话框）、Ctrl+B/I/U（阻止富文本格式操作）
    /// 以及 Tab（实现自定义缩进功能）。
    /// </remarks>
    private void CodeEditor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        CoreVirtualKeyStates ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        bool isCtrlDown = (ctrlState & CoreVirtualKeyStates.Down) != 0;

        if (isCtrlDown)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                HandleExecuteRequest();
                return;
            }

            if (e.Key == VirtualKey.E)
            {
                e.Handled = true;
                _ = HandleArgsRequestAsync();
                return;
            }

            if (e.Key is VirtualKey.B or VirtualKey.I or VirtualKey.U)
            {
                e.Handled = true;
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
    /// 处理 Ctrl+E 快捷键，打开命令行参数对话框（Page 级后备，当 RichEditBox 未聚焦时生效）。
    /// </summary>
    /// <param name="sender">快捷键加速器对象。</param>
    /// <param name="args">快捷键调用事件参数。</param>
    private void ArgsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = HandleArgsRequestAsync();
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

    #region 命令行参数

    /// <summary>
    /// 显示命令行参数输入对话框，供用户设置传递给脚本的参数。
    /// </summary>
    /// <returns>表示异步对话框操作的任务。</returns>
    /// <remarks>
    /// 参数仅在内存中保持，不持久化存储，应用关闭即丢失。
    /// 支持 Enter 键确认、"清除"按钮清空参数。
    /// 对话框关闭后通知 MainWindow 更新指示点状态。
    /// </remarks>
    public async Task HandleArgsRequestAsync()
    {
        if (XamlRoot is null)
        {
            return;
        }

        TextBox argsTextBox = new()
        {
            Text = ViewModel.CommandLineArguments,
            PlaceholderText = "e.g. --input data.txt --verbose",
            AcceptsReturn = false,
            MinWidth = 400,
        };

        ContentDialog dialog = new()
        {
            Title = Text.Localize("命令行参数"),
            Content = argsTextBox,
            PrimaryButtonText = Text.Localize("确定"),
            SecondaryButtonText = Text.Localize("清除"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        bool confirmedViaEnter = false;
        argsTextBox.PreviewKeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == VirtualKey.Enter)
            {
                keyArgs.Handled = true;
                confirmedViaEnter = true;
                dialog.Hide();
            }
        };

        ContentDialogResult result = await dialog.ShowAsync();

        if (result is ContentDialogResult.Primary || confirmedViaEnter)
        {
            ViewModel.CommandLineArguments = argsTextBox.Text;
        }
        else if (result is ContentDialogResult.Secondary)
        {
            ViewModel.CommandLineArguments = string.Empty;
        }

        NotifyMainWindowArgsDotChanged();
    }

    /// <summary>
    /// 通知 MainWindow 更新命令行参数指示点的可见性。
    /// </summary>
    private void NotifyMainWindowArgsDotChanged()
    {
        if (Application.Current is App { MainWindow: MainWindow mw })
        {
            mw.UpdateArgsDotVisibility(ViewModel.HasCommandLineArguments);
        }
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
    /// 根据编辑器是否有内容更新占位提示的可见性。
    /// </summary>
    private void UpdatePlaceholderVisibility()
    {
        string text = GetPlainText();
        PlaceholderPanel.Visibility = string.IsNullOrEmpty(text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// 更新占位提示的本地化文本。
    /// </summary>
    private void UpdatePlaceholderText()
    {
        PlaceholderText.Text = Text.Localize("在此粘贴代码");
        PlaceholderSubText.Text = Text.Localize("右键或Ctrl+V");
    }

    /// <summary>
    /// 刷新本地化文本，供 MainWindow 在语言切换后调用。
    /// </summary>
    public void RefreshLocalizedTexts()
    {
        ViewModel.RefreshLocalizedTexts();
        UpdatePlaceholderText();
    }

    #endregion
}