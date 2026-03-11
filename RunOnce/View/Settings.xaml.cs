/*
 * 设置页面视图
 * 提供应用程序配置界面的 View 层实现，负责本地化文本、对话框展示与主题应用
 *
 * @author: WaterRun
 * @file: View/Settings.xaml.cs
 * @date: 2026-03-11
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RunOnce.Static;
using RunOnce.ViewModel;
using Windows.System;

namespace RunOnce.View;

/// <summary>
/// 设置页面，提供应用程序所有配置项的可视化编辑界面。
/// </summary>
/// <remarks>
/// 不变量：<see cref="ViewModel"/> 在构造时创建，生命周期与页面一致；所有数据绑定通过 x:Bind 建立。
/// 线程安全：所有成员必须在 UI 线程访问。
/// 副作用：本地化文本在页面加载及语言切换时更新；对话框通过 code-behind 展示。
/// </remarks>
public sealed partial class Settings : Page
{
    /// <summary>
    /// 设置页面的 ViewModel 实例，承载所有可绑定状态与业务逻辑。
    /// </summary>
    public SettingsViewModel ViewModel { get; }

    /// <summary>
    /// 初始化设置页面实例，创建 ViewModel 并注册事件。
    /// </summary>
    public Settings()
    {
        ViewModel = new SettingsViewModel();
        ViewModel.ThemeChanged += OnThemeChanged;
        ViewModel.LanguageChanged += OnLanguageChanged;
        ViewModel.ScriptPlacementChangeRequested += OnScriptPlacementChangeRequested;
        InitializeComponent();
        Loaded += HandlePageLoaded;
    }

    /// <summary>
    /// 处理页面加载完成事件，执行初始化显示逻辑。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private void HandlePageLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalizedTexts();
        RefreshStoreRowVisibility();
    }

    #region 事件回调

    /// <summary>
    /// 处理 ViewModel 的主题变更通知，将新主题应用到窗口。
    /// </summary>
    /// <param name="theme">新选中的主题风格。</param>
    private static void OnThemeChanged(ThemeStyle theme)
    {
        if (Application.Current is App app)
        {
            app.ApplyTheme(theme);
        }
    }

    /// <summary>
    /// 处理 ViewModel 的语言变更通知，刷新界面本地化内容。
    /// </summary>
    private void OnLanguageChanged()
    {
        ViewModel.RefreshAfterLanguageChange();
        ApplyLocalizedTexts();
        RefreshStoreRowVisibility();
    }

    /// <summary>
    /// 处理 ViewModel 的脚本放置行为变更请求，弹出确认对话框。
    /// </summary>
    /// <param name="oldIndex">变更前的选项索引。</param>
    /// <param name="newIndex">变更后的选项索引。</param>
    /// <remarks>
    /// 用户确认后将变更写入 Config；用户取消后恢复 ComboBox 至原选项。
    /// </remarks>
    private async void OnScriptPlacementChangeRequested(int oldIndex, int newIndex)
    {
        if (XamlRoot is null)
        {
            ViewModel.RevertScriptPlacement(oldIndex);
            return;
        }

        string message = (ScriptPlacementBehavior)newIndex switch
        {
            ScriptPlacementBehavior.EnsureCompatibility =>
                Text.Localize("此操作将把临时代码文件放置在工作目录，当异常关闭时，可能无法有效的清理。"),
            ScriptPlacementBehavior.EnsureCleanup =>
                Text.Localize("此操作将把临时代码文件放置在临时目录，可能产生一些兼容性问题。"),
            _ => string.Empty,
        };

        ContentDialog confirmDialog = new()
        {
            Title = Text.Localize("脚本放置行为"),
            Content = message,
            PrimaryButtonText = Text.Localize("确定"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        ContentDialogResult result = await confirmDialog.ShowAsync();

        if (result is ContentDialogResult.Primary)
        {
            ViewModel.ConfirmScriptPlacement(newIndex);
        }
        else
        {
            ViewModel.RevertScriptPlacement(oldIndex);
        }
    }

    #endregion

    #region 本地化文本

    /// <summary>
    /// 将所有标签和描述文本更新为当前语言的本地化版本。
    /// </summary>
    private void ApplyLocalizedTexts()
    {
        PageTitle.Text = Text.Localize("设置");

        BasicSectionHeader.Text = Text.Localize("基本");
        ExecutionSectionHeader.Text = Text.Localize("代码执行");

        ThemeLabel.Text = Text.Localize("外观");
        ThemeDescription.Text = Text.Localize("选择应用程序的主题风格");
        LanguageLabel.Text = Text.Localize("语言");
        LanguageDescription.Text = Text.Localize("选择应用程序的显示语言");

        ConfirmLabel.Text = Text.Localize("执行前确认");
        ConfirmDescription.Text = Text.Localize("执行代码前显示确认对话框");
        SelectorModeLabel.Text = Text.Localize("执行前语言选择框");
        SelectorModeDescription.Text = Text.Localize("控制语言选择框的显示时机");
        AutoExitLabel.Text = Text.Localize("执行时自动退出");
        AutoExitDescription.Text = Text.Localize("开始执行代码后自动关闭应用程序");
        AutoCloseTerminalLabel.Text = Text.Localize("运行完毕后自动关闭终端");
        AutoCloseTerminalDescription.Text = Text.Localize("代码运行完成后自动关闭终端窗口");
        ShellLabel.Text = Text.Localize("运行环境");
        ShellDescription.Text = Text.Localize("选择执行代码使用的命令解释器");
        ScriptPlacementLabel.Text = Text.Localize("脚本放置行为");
        ScriptPlacementDescription.Text = Text.Localize("选择临时代码文件的放置位置");
        ShortcutsLabel.Text = Text.Localize("快捷键");
        ShortcutsDescription.Text = Text.Localize("查看应用程序支持的快捷键");
        ShortcutsButton.Content = Text.Localize("查看");
        AdvancedSettingsLabel.Text = Text.Localize("高级设置");
        AdvancedSettingsDescription.Text = Text.Localize("配置临时文件、置信度阈值和语言命令");
        AdvancedSettingsButton.Content = Text.Localize("打开");

        ApplyWideLocalizedTexts();
        ApplyNarrowAboutLocalizedTexts();
    }

    /// <summary>
    /// 更新宽屏左侧面板中的本地化文本。
    /// </summary>
    private void ApplyWideLocalizedTexts()
    {
        WideStoreLink.Content = Text.Localize("微软商店");
        WideResetLink.Content = Text.Localize("重置所有设置");
    }

    /// <summary>
    /// 更新窄屏关于区域中的本地化文本。
    /// </summary>
    private void ApplyNarrowAboutLocalizedTexts()
    {
        NarrowAboutSectionHeader.Text = Text.Localize("此程序");
        NarrowAppNameLabel.Text = Text.Localize("软件名");
        NarrowVersionLabel.Text = Text.Localize("版本");
        NarrowBuildTimeLabel.Text = Text.Localize("编译于");
        NarrowAuthorLabel.Text = Text.Localize("作者");
        NarrowGitHubLink.Content = Text.Localize("访问");
        NarrowStoreLabel.Text = Text.Localize("微软商店");
        NarrowStoreLink.Content = Text.Localize("访问");
        NarrowResetLink.Content = Text.Localize("重置所有设置");
    }

    #endregion

    #region 可见性管理

    /// <summary>
    /// 根据商店 URL 是否可用刷新商店行的可见性。
    /// </summary>
    private void RefreshStoreRowVisibility()
    {
        Visibility storeVisibility = ViewModel.HasStoreUrl ? Visibility.Visible : Visibility.Collapsed;
        NarrowStoreRow.Visibility = storeVisibility;
        WideStoreLink.Visibility = storeVisibility;
    }

    #endregion

    #region 快捷键对话框

    /// <summary>
    /// 处理快捷键查看按钮点击事件，弹出快捷键信息对话框。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private async void ShortcutsButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = BuildShortcutsDialog();
        await dialog.ShowAsync();
    }

    /// <summary>
    /// 构建快捷键信息对话框。
    /// </summary>
    /// <returns>配置完成的 <see cref="ContentDialog"/> 实例。</returns>
    private ContentDialog BuildShortcutsDialog()
    {
        StackPanel panel = new() { Spacing = 8, MinWidth = 380 };

        AddShortcutRow(panel, "Ctrl+Enter", Text.Localize("执行代码"));
        AddShortcutRow(panel, "Ctrl+E", Text.Localize("命令行参数"));
        // AddShortcutRow(panel, "Ctrl+Z", Text.Localize("撤销"));
        AddShortcutRow(panel, "Ctrl+Y", Text.Localize("重做"));
        AddShortcutRow(panel, "Ctrl+A", Text.Localize("全选"));
        AddShortcutRow(panel, "Ctrl+C", Text.Localize("复制"));
        AddShortcutRow(panel, "Ctrl+V", Text.Localize("粘贴"));
        AddShortcutRow(panel, "Ctrl+X", Text.Localize("剪切"));
        AddShortcutRow(panel, "Tab", Text.Localize("缩进"));
        AddShortcutRow(panel, "Shift+Tab", Text.Localize("减少缩进"));

        return new ContentDialog
        {
            Title = Text.Localize("快捷键"),
            Content = panel,
            CloseButtonText = Text.Localize("关闭"),
            XamlRoot = XamlRoot,
        };
    }

    /// <summary>
    /// 向面板添加一行快捷键信息。
    /// </summary>
    /// <param name="panel">目标面板。</param>
    /// <param name="shortcut">快捷键文本。</param>
    /// <param name="description">功能描述文本。</param>
    private static void AddShortcutRow(StackPanel panel, string shortcut, string description)
    {
        Grid row = new() { Padding = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        TextBlock keyBlock = new()
        {
            Text = shortcut,
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.85,
        };
        Grid.SetColumn(keyBlock, 0);
        row.Children.Add(keyBlock);

        TextBlock descBlock = new()
        {
            Text = description,
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(descBlock, 1);
        row.Children.Add(descBlock);

        panel.Children.Add(row);
    }

    #endregion

    #region 高级设置对话框

    /// <summary>
    /// 处理高级设置按钮点击事件，弹出高级设置对话框。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private async void AdvancedSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = BuildAdvancedSettingsDialog();
        await dialog.ShowAsync();
    }

    /// <summary>
    /// 构建高级设置对话框。
    /// </summary>
    /// <returns>配置完成的 <see cref="ContentDialog"/> 实例。</returns>
    private ContentDialog BuildAdvancedSettingsDialog()
    {
        StackPanel contentPanel = new() { Spacing = 16, MinWidth = 450, Margin = new Thickness(0, 0, 8, 0) };

        TextBox prefixTextBox = new()
        {
            Header = Text.Localize("临时文件名前缀"),
            Text = ViewModel.GetTempFilePrefix(),
            PlaceholderText = Config.DefaultTempFilePrefix,
        };
        contentPanel.Children.Add(prefixTextBox);

        StackPanel thresholdPanel = new() { Spacing = 8 };
        thresholdPanel.Children.Add(new TextBlock
        {
            Text = Text.Localize("置信度阈值"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        NumberBox thresholdBox = new()
        {
            Value = ViewModel.GetConfidenceThreshold(),
            Minimum = 0,
            Maximum = 1,
            SmallChange = 0.01,
            LargeChange = 0.1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        thresholdPanel.Children.Add(thresholdBox);
        contentPanel.Children.Add(thresholdPanel);

        (StackPanel commandsPanel, Dictionary<string, TextBox> commandTextBoxes) = BuildLanguageCommandControls();
        contentPanel.Children.Add(commandsPanel);

        HyperlinkButton resetLink = BuildResetAdvancedLink(prefixTextBox, thresholdBox, commandTextBoxes);
        contentPanel.Children.Add(resetLink);

        TextBlock errorText = new()
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0),
        };
        contentPanel.Children.Add(errorText);

        ScrollViewer scrollViewer = new()
        {
            Content = contentPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = Math.Max(200, XamlRoot.Size.Height - 200),
            Padding = new Thickness(0, 0, 16, 0),
        };

        ContentDialog dialog = new()
        {
            Title = Text.Localize("高级设置"),
            Content = scrollViewer,
            PrimaryButtonText = Text.Localize("保存"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            errorText.Visibility = Visibility.Collapsed;

            try
            {
                Dictionary<string, string> commands = commandTextBoxes.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Text);

                ViewModel.SaveAdvancedSettings(prefixTextBox.Text, thresholdBox.Value, commands);
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                errorText.Text = ex.Message;
                errorText.Visibility = Visibility.Visible;
            }
        };

        return dialog;
    }

    /// <summary>
    /// 构建语言执行命令控件组。
    /// </summary>
    /// <returns>包含面板和命令文本框字典的元组。</returns>
    private static (StackPanel Panel, Dictionary<string, TextBox> TextBoxes) BuildLanguageCommandControls()
    {
        StackPanel panel = new() { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = Text.Localize("语言执行命令"),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });

        Dictionary<string, string> currentCommands = Config.GetAllLanguageCommands();

        Dictionary<string, TextBox> textBoxes = Config.SupportedLanguages.ToDictionary(
            language => language,
            language =>
            {
                string currentCommand = currentCommands.GetValueOrDefault(language, language);
                TextBox textBox = new()
                {
                    Header = new TextBlock
                    {
                        Text = language.ToUpperInvariant(),
                        FontSize = 13,
                        FontStyle = Windows.UI.Text.FontStyle.Italic,
                        Opacity = 0.6,
                    },
                    Text = currentCommand,
                    PlaceholderText = currentCommand,
                };
                panel.Children.Add(textBox);
                return textBox;
            });

        return (panel, textBoxes);
    }

    /// <summary>
    /// 构建高级设置重置链接按钮。
    /// </summary>
    /// <param name="prefixTextBox">临时文件前缀文本框。</param>
    /// <param name="thresholdBox">置信度阈值数值框。</param>
    /// <param name="commandTextBoxes">语言命令文本框字典。</param>
    /// <returns>配置完成的 <see cref="HyperlinkButton"/> 实例。</returns>
    private HyperlinkButton BuildResetAdvancedLink(
        TextBox prefixTextBox,
        NumberBox thresholdBox,
        Dictionary<string, TextBox> commandTextBoxes)
    {
        HyperlinkButton resetLink = new()
        {
            Content = Text.Localize("重置为默认"),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        resetLink.Click += (_, _) =>
        {
            (string prefix, double threshold, Dictionary<string, string> commands) = ViewModel.ResetAdvancedToDefaults();

            prefixTextBox.Text = prefix;
            thresholdBox.Value = threshold;

            foreach ((string language, TextBox textBox) in commandTextBoxes)
            {
                textBox.Text = commands.GetValueOrDefault(language, language);
            }
        };

        return resetLink;
    }

    /// <summary>
    /// 处理重置所有设置链接点击事件。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private async void ResetAllLink_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog confirmDialog = new()
        {
            Title = Text.Localize("重置所有设置"),
            Content = Text.Localize("确定要将所有设置重置为默认值吗？此操作无法撤销。"),
            PrimaryButtonText = Text.Localize("重置"),
            CloseButtonText = Text.Localize("取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        ContentDialogResult result = await confirmDialog.ShowAsync();
        if (result is not ContentDialogResult.Primary)
        {
            return;
        }

        ViewModel.ResetAllSettings();

        if (Application.Current is App app)
        {
            app.ApplyTheme(Config.Theme);
        }

        ApplyLocalizedTexts();
        RefreshStoreRowVisibility();
    }

    #endregion

    #region 外部链接

    /// <summary>
    /// 处理 GitHub 链接点击事件。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private async void GitHubLink_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.GitHubUrl))
        {
            await Launcher.LaunchUriAsync(new Uri(ViewModel.GitHubUrl));
        }
    }

    /// <summary>
    /// 处理微软商店链接点击事件。
    /// </summary>
    /// <param name="sender">事件源对象。</param>
    /// <param name="e">路由事件参数。</param>
    private async void StoreLink_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasStoreUrl)
        {
            await Launcher.LaunchUriAsync(new Uri(ViewModel.StoreUrl));
        }
    }

    #endregion
}