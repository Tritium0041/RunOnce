/*
 * 脚本执行管理
 * 提供临时脚本文件生成、终端执行及临时文件清理功能
 * 临时文件根据配置存放于系统临时目录或工作目录，执行完成后由终端命令自动清理，应用启动时兜底清扫残留文件
 *
 * @author: WaterRun
 * @file: Static/Exec.cs
 * @date: 2026-03-09
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace RunOnce.Static;

/// <summary>
/// 脚本执行管理静态类，提供临时脚本文件生成与终端执行功能。
/// </summary>
/// <remarks>
/// 不变量：临时文件根据 <see cref="Config.ScriptPlacement"/> 配置创建在系统临时目录或工作目录下，
/// 文件名包含随机后缀以避免冲突。执行命令末尾包含清理指令以确保临时文件被删除。
/// 线程安全：所有公开方法为线程安全，内部字典为只读。
/// 副作用：会在文件系统创建临时文件，启动外部终端进程。
/// </remarks>
public static class Exec
{
    /// <summary>
    /// 语言标识符到文件扩展名的映射字典，键为小写语言标识符，值为包含点号的扩展名。
    /// </summary>
    private static readonly Dictionary<string, string> _languageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bat"] = ".bat",
        ["powershell"] = ".ps1",
        ["python"] = ".py",
        ["lua"] = ".lua",
        ["nim"] = ".nim",
        ["go"] = ".go",
    };

    /// <summary>
    /// 获取指定语言对应的文件扩展名。
    /// </summary>
    /// <param name="language">
    /// 脚本语言标识符，必须是 <see cref="Config.SupportedLanguages"/> 中定义的有效值，不区分大小写。
    /// 不允许为 null 或空白字符串。
    /// </param>
    /// <returns>该语言对应的文件扩展名，包含前导点号（如 ".py"）。</returns>
    /// <exception cref="ArgumentNullException">当 language 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当 language 为空白字符串或不在支持列表中时抛出。</exception>
    public static string GetFileExtension(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException(Text.Localize("语言标识符不能为空白字符串。"), nameof(language));
        }

        string normalizedLanguage = language.ToLowerInvariant();
        if (!_languageExtensions.TryGetValue(normalizedLanguage, out string? extension))
        {
            throw new ArgumentException(Text.Localize("不支持的语言标识符: {0}。", language), nameof(language));
        }

        return extension;
    }

    /// <summary>
    /// 获取所有支持语言的文件扩展名映射副本。
    /// </summary>
    /// <returns>包含所有语言及其对应文件扩展名的字典副本。</returns>
    public static Dictionary<string, string> GetAllFileExtensions()
    {
        return new Dictionary<string, string>(_languageExtensions, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 根据配置生成临时脚本文件，启动终端执行脚本，执行完成后自动清理临时文件。
    /// </summary>
    /// <param name="code">要执行的脚本代码内容，不允许为 null 或空字符串。</param>
    /// <param name="language">
    /// 脚本语言标识符，必须是 <see cref="Config.SupportedLanguages"/> 中定义的有效值，不区分大小写。
    /// 不允许为 null 或空白字符串。
    /// </param>
    /// <param name="workingDirectory">脚本执行的工作目录路径，不允许为 null 或空白字符串，目录必须存在。</param>
    /// <exception cref="ArgumentNullException">当 code、language 或 workingDirectory 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当参数为空白字符串、language 不在支持列表中、或工作目录不存在时抛出。</exception>
    /// <exception cref="IOException">当无法创建临时文件时抛出。</exception>
    /// <exception cref="InvalidOperationException">当无法启动终端进程时抛出。</exception>
    /// <remarks>
    /// 此方法为 fire-and-forget 模式，启动终端进程后立即返回。
    /// 临时文件的清理由终端命令自行完成，不依赖本程序的后续执行。
    /// 当 <see cref="Config.ScriptPlacement"/> 为 EnsureCleanup 时文件创建在系统临时目录，
    /// 为 EnsureCompatibility 时文件创建在工作目录。
    /// </remarks>
    public static void Execute(string code, string language, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        if (string.IsNullOrEmpty(code))
        {
            throw new ArgumentException(Text.Localize("代码内容不能为空。"), nameof(code));
        }
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException(Text.Localize("语言标识符不能为空白字符串。"), nameof(language));
        }
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException(Text.Localize("工作目录不能为空白字符串。"), nameof(workingDirectory));
        }
        if (!Directory.Exists(workingDirectory))
        {
            throw new ArgumentException(Text.Localize("工作目录不存在: {0}。", workingDirectory), nameof(workingDirectory));
        }

        string tempFileDirectory = Config.ScriptPlacement switch
        {
            ScriptPlacementBehavior.EnsureCompatibility => workingDirectory,
            _ => Path.GetTempPath(),
        };

        string tempFilePath = CreateTempFile(code, language, tempFileDirectory);
        string languageCommand = Config.GetLanguageCommand(language);
        (string shellExe, string shellArgs) = BuildShellLaunchInfo(languageCommand, tempFilePath);

        LaunchInTerminal(shellExe, shellArgs, workingDirectory);
    }

    /// <summary>
    /// 清理系统临时目录中由本应用创建的残留临时文件。
    /// </summary>
    /// <remarks>
    /// 扫描 <see cref="Path.GetTempPath"/> 下所有以 <see cref="Config.TempFilePrefix"/> 开头的文件并尝试删除。
    /// 被锁定的文件（正在被其他进程使用）会被静默跳过。
    /// 仅清理系统临时目录中的残留文件；放置在工作目录中的文件不在此方法清理范围内。
    /// 建议在应用启动时调用一次。
    /// </remarks>
    public static void CleanupStaleTempFiles()
    {
        try
        {
            string tempDir = Path.GetTempPath();
            string prefix = Config.TempFilePrefix;

            foreach (string file in Directory.EnumerateFiles(tempDir, $"{prefix}*"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // CLEANUP-001: 被锁定或无权限的文件静默跳过
                }
            }
        }
        catch
        {
            // CLEANUP-002: 临时目录不可枚举时静默跳过
        }
    }

    /// <summary>
    /// 在指定目录下创建临时脚本文件。
    /// </summary>
    /// <param name="code">脚本代码内容，已验证非空。</param>
    /// <param name="language">脚本语言标识符，已验证有效。</param>
    /// <param name="baseDirectory">临时文件的存放目录路径。</param>
    /// <returns>创建的临时文件的完整路径。</returns>
    /// <exception cref="IOException">当文件创建失败时抛出。</exception>
    private static string CreateTempFile(string code, string language, string baseDirectory)
    {
        string extension = GetFileExtension(language);
        string uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        string fileName = Config.TempFilePrefix + uniqueSuffix + extension;
        string filePath = Path.Combine(baseDirectory, fileName);

        try
        {
            File.WriteAllText(filePath, code, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            throw new IOException(Text.Localize("无法创建临时文件: {0}。", filePath), ex);
        }

        return filePath;
    }

    /// <summary>
    /// 根据当前 Shell 配置构建命令解释器的启动信息。
    /// </summary>
    /// <param name="languageCommand">语言执行指令（如 "python"、"cmd /c"）。</param>
    /// <param name="tempFilePath">临时脚本文件的完整路径。</param>
    /// <returns>Shell 可执行文件名与启动参数的元组。</returns>
    private static (string ShellExe, string ShellArgs) BuildShellLaunchInfo(string languageCommand, string tempFilePath)
    {
        return Config.Shell switch
        {
            ShellType.PowerShell => BuildPowerShellLaunchInfo("powershell.exe", languageCommand, tempFilePath, forceUtf8: false),
            ShellType.PowerShellUtf8 => BuildPowerShellLaunchInfo("powershell.exe", languageCommand, tempFilePath, forceUtf8: true),
            ShellType.Pwsh => BuildPowerShellLaunchInfo("pwsh.exe", languageCommand, tempFilePath, forceUtf8: false),
            ShellType.Cmd => BuildCmdLaunchInfo(languageCommand, tempFilePath, forceUtf8: false),
            ShellType.CmdUtf8 => BuildCmdLaunchInfo(languageCommand, tempFilePath, forceUtf8: true),
            _ => BuildPowerShellLaunchInfo("powershell.exe", languageCommand, tempFilePath, forceUtf8: false),
        };
    }

    /// <summary>
    /// 构建 PowerShell 系列 Shell 的启动信息。
    /// </summary>
    /// <param name="exe">PowerShell 可执行文件名（powershell.exe 或 pwsh.exe）。</param>
    /// <param name="languageCommand">语言执行指令。</param>
    /// <param name="tempFilePath">临时脚本文件的完整路径。</param>
    /// <param name="forceUtf8">是否在脚本开头强制设置 UTF-8 编码。</param>
    /// <returns>Shell 可执行文件名与启动参数的元组。</returns>
    /// <remarks>
    /// 使用 -EncodedCommand 传递 Base64 编码的脚本，避免所有命令行引号转义问题。
    /// 脚本执行顺序：可选 UTF-8 设置 → 执行语言指令 → 等待用户按 Enter → 删除临时文件。
    /// </remarks>
    private static (string ShellExe, string ShellArgs) BuildPowerShellLaunchInfo(
        string exe, string languageCommand, string tempFilePath, bool forceUtf8)
    {
        string escapedPath = tempFilePath.Replace("'", "''");
        string exitPrompt = Text.Localize("按 Enter 键退出").Replace("'", "''");

        StringBuilder scriptBuilder = new();

        if (forceUtf8)
        {
            scriptBuilder.Append("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; ");
            scriptBuilder.Append("[Console]::InputEncoding = [System.Text.Encoding]::UTF8; ");
        }

        scriptBuilder.Append($"{languageCommand} '{escapedPath}'; ");
        scriptBuilder.Append("Write-Host ''; ");
        scriptBuilder.Append($"Read-Host '{exitPrompt}'; ");
        scriptBuilder.Append($"Remove-Item -LiteralPath '{escapedPath}' -Force -ErrorAction SilentlyContinue");

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(scriptBuilder.ToString()));

        return (exe, $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}");
    }

    /// <summary>
    /// 构建 CMD 系列 Shell 的启动信息。
    /// </summary>
    /// <param name="languageCommand">语言执行指令。</param>
    /// <param name="tempFilePath">临时脚本文件的完整路径。</param>
    /// <param name="forceUtf8">是否在命令开头强制切换到 UTF-8 代码页。</param>
    /// <returns>Shell 可执行文件名与启动参数的元组。</returns>
    /// <remarks>
    /// 命令执行顺序：可选 chcp 65001 → 执行语言指令 → pause 等待按键 → 删除临时文件。
    /// 使用 /c 参数，命令序列执行完毕后 CMD 自动退出。
    /// </remarks>
    private static (string ShellExe, string ShellArgs) BuildCmdLaunchInfo(
        string languageCommand, string tempFilePath, bool forceUtf8)
    {
        string quotedPath = $"\"{tempFilePath}\"";

        StringBuilder commandBuilder = new();

        if (forceUtf8)
        {
            commandBuilder.Append("chcp 65001 >nul & ");
        }

        commandBuilder.Append($"{languageCommand} {quotedPath} & echo. & pause & del /f /q {quotedPath}");

        return ("cmd.exe", $"/c \"{commandBuilder}\"");
    }

    /// <summary>
    /// 根据配置的终端类型启动终端进程执行命令。
    /// </summary>
    /// <param name="shellExe">命令解释器可执行文件名。</param>
    /// <param name="shellArgs">命令解释器的启动参数。</param>
    /// <param name="workingDirectory">终端的工作目录。</param>
    /// <exception cref="InvalidOperationException">当终端进程启动失败时抛出。</exception>
    private static void LaunchInTerminal(string shellExe, string shellArgs, string workingDirectory)
    {
        ProcessStartInfo startInfo = Config.Terminal switch
        {
            TerminalType.WindowsTerminal => CreateWindowsTerminalStartInfo(shellExe, shellArgs, workingDirectory),
            TerminalType.Cmd => CreateLegacyTerminalStartInfo(shellExe, shellArgs, workingDirectory),
            _ => CreateWindowsTerminalStartInfo(shellExe, shellArgs, workingDirectory),
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException(Text.Localize("无法启动终端进程。"), ex);
        }
    }

    /// <summary>
    /// 创建 Windows Terminal 的进程启动信息。
    /// </summary>
    /// <param name="shellExe">命令解释器可执行文件名。</param>
    /// <param name="shellArgs">命令解释器的启动参数。</param>
    /// <param name="workingDirectory">工作目录路径。</param>
    /// <returns>配置完成的 <see cref="ProcessStartInfo"/> 实例。</returns>
    private static ProcessStartInfo CreateWindowsTerminalStartInfo(string shellExe, string shellArgs, string workingDirectory)
    {
        return new ProcessStartInfo
        {
            FileName = Config.WindowsTerminalExecutable,
            Arguments = $"-d \"{workingDirectory}\" {shellExe} {shellArgs}",
            UseShellExecute = true,
            CreateNoWindow = false,
        };
    }

    /// <summary>
    /// 创建传统终端（直接启动 Shell 进程）的进程启动信息。
    /// </summary>
    /// <param name="shellExe">命令解释器可执行文件名。</param>
    /// <param name="shellArgs">命令解释器的启动参数。</param>
    /// <param name="workingDirectory">工作目录路径。</param>
    /// <returns>配置完成的 <see cref="ProcessStartInfo"/> 实例。</returns>
    private static ProcessStartInfo CreateLegacyTerminalStartInfo(string shellExe, string shellArgs, string workingDirectory)
    {
        return new ProcessStartInfo
        {
            FileName = shellExe,
            Arguments = shellArgs,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            CreateNoWindow = false,
        };
    }
}
