/*
 * 脚本语言检测器
 * 通过代码特征分析自动识别脚本语言类型，输出按置信度排序的检测结果
 *
 * @author: WaterRun
 * @file: Static/LanguageDetector.cs
 * @date: 2026-03-04
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RunOnce.Static;

/// <summary>
/// 语言检测结果记录，承载单个语言的检测置信度信息。
/// </summary>
/// <remarks>
/// 不变量：Language 必须是 <see cref="Config.SupportedLanguages"/> 中的有效值；Confidence 范围为 [0.0, 1.0]。
/// 线程安全：作为不可变记录类型，天然线程安全。
/// 副作用：无。
/// </remarks>
/// <param name="Language">语言标识符，与 <see cref="Config.SupportedLanguages"/> 中的定义一致。</param>
/// <param name="Confidence">检测置信度，范围 [0.0, 1.0]，值越高表示越可能是该语言。</param>
public readonly record struct DetectionResult(string Language, double Confidence)
{
    /// <summary>
    /// 判断当前结果是否达到可信标准。
    /// </summary>
    /// <param name="threshold">置信度判定阈值，范围 [0.0, 1.0]。</param>
    /// <returns>若置信度大于等于阈值则返回 true，否则返回 false。</returns>
    public bool IsConfident(double threshold) => Confidence >= threshold;
}

/// <summary>
/// 脚本语言检测器静态类，通过代码特征分析自动识别脚本语言类型。
/// </summary>
/// <remarks>
/// 不变量：所有检测规则为硬编码且不可变；检测结果始终覆盖 <see cref="Config.SupportedLanguages"/> 中的全部语言。
/// 线程安全：所有公开方法为线程安全，内部状态均为只读。
/// 副作用：无。
/// </remarks>
public static class LanguageDetector
{
    /// <summary>
    /// 确定性标记命中时的置信度分数。
    /// </summary>
    private const double DefinitiveScore = 0.98;

    /// <summary>
    /// 强特征组合最高分数上限。
    /// </summary>
    private const double StrongFeatureMaxScore = 0.92;

    /// <summary>
    /// 弱特征单项分数。
    /// </summary>
    private const double WeakFeatureScore = 0.06;

    /// <summary>
    /// 弱特征最高分数上限。
    /// </summary>
    private const double WeakFeatureMaxScore = 0.30;

    #region Shebang 检测

    /// <summary>
    /// Shebang 行匹配正则表达式，预编译以提升性能。
    /// </summary>
    private static readonly Regex _shebangRegex = new(
        @"^#!\s*/(?:usr/(?:local/)?)?bin/(?:env\s+)?(\w+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Shebang 解释器名称到语言标识符的映射字典。
    /// </summary>
    private static readonly Dictionary<string, string> _shebangMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] = "python",
        ["python3"] = "python",
        ["python2"] = "python",
        ["lua"] = "lua",
        ["pwsh"] = "powershell",
        ["powershell"] = "powershell",
        ["bash"] = "bat",
        ["sh"] = "bat",
        ["nim"] = "nim",
        ["go"] = "go",
    };

    #endregion

    #region 确定性标记正则表达式

    // ── Python ──

    /// <summary>
    /// Python 入口守卫模式：if __name__ == '__main__'。
    /// </summary>
    private static readonly Regex _rxPyNameMain = new(
        @"if\s+__name__\s*==\s*['""]__main__['""]",
        RegexOptions.Compiled);

    /// <summary>
    /// Python 函数定义模式：def xxx(...):，行首起始，以冒号结尾。
    /// </summary>
    private static readonly Regex _rxPyDefColon = new(
        @"^\s*def\s+\w+\s*\([^)]*\)\s*:",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Python 类定义模式：class xxx: 或 class xxx(Base):，行尾冒号且无花括号。
    /// </summary>
    private static readonly Regex _rxPyClassColon = new(
        @"^\s*class\s+\w+[^{]*:\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Python 带点号的 from 导入模式：from xxx.yyy import zzz，仅 Python 使用点号模块路径。
    /// </summary>
    private static readonly Regex _rxPyFromDotImport = new(
        @"^\s*from\s+\w+\.\w[\w.]*\s+import\b",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Python dunder 方法定义模式：def __xxx__(...。
    /// </summary>
    private static readonly Regex _rxPyDunder = new(
        @"\bdef\s+__\w+__\s*\(",
        RegexOptions.Compiled);

    // ── PowerShell ──

    /// <summary>
    /// PowerShell CmdletBinding 特性模式。
    /// </summary>
    private static readonly Regex _rxPsCmdletBinding = new(
        @"\[CmdletBinding\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// PowerShell Parameter 特性模式。
    /// </summary>
    private static readonly Regex _rxPsParameter = new(
        @"\[Parameter\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// PowerShell 内置自动变量模式。
    /// </summary>
    private static readonly Regex _rxPsBuiltinVar = new(
        @"\$(?:PSVersionTable|PSScriptRoot|PSCommandPath|PSCmdlet|ErrorActionPreference)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// PowerShell 美元符号变量赋值模式。
    /// </summary>
    private static readonly Regex _rxPsDollarAssign = new(
        @"\$[A-Za-z_]\w*\s*=",
        RegexOptions.Compiled);

    /// <summary>
    /// PowerShell 标准 Cmdlet 动词-名词模式。
    /// </summary>
    private static readonly Regex _rxPsCmdlet = new(
        @"\b(?:Get|Set|New|Remove|Add|Import|Export|Invoke|Start|Stop|Test|Write|Read|Out|Select|Where|ForEach|Sort|Group|Measure|Compare|ConvertTo|ConvertFrom)-[A-Z]\w+",
        RegexOptions.Compiled);

    // ── Bat ──

    /// <summary>
    /// Bat @echo off 模式。
    /// </summary>
    private static readonly Regex _rxBatEchoOff = new(
        @"^\s*@echo\s+off\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>
    /// Bat setlocal enabledelayedexpansion 模式。
    /// </summary>
    private static readonly Regex _rxBatSetLocalDelayed = new(
        @"\bsetlocal\s+enabledelayedexpansion\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Bat 百分号变量引用模式（%VAR%），排除 URL 编码。
    /// </summary>
    private static readonly Regex _rxBatVarRef = new(
        @"%[A-Za-z_]\w*%",
        RegexOptions.Compiled);

    /// <summary>
    /// Bat for 循环 %%变量 模式。
    /// </summary>
    private static readonly Regex _rxBatForVar = new(
        @"%%~?[A-Za-z]",
        RegexOptions.Compiled);

    /// <summary>
    /// Bat goto 标签模式。
    /// </summary>
    private static readonly Regex _rxBatGoto = new(
        @"^\s*goto\s+:\w+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // ── Lua ──

    /// <summary>
    /// Lua local function 模式，仅 Lua 在此六种语言中使用 local 关键字。
    /// </summary>
    private static readonly Regex _rxLuaLocalFunc = new(
        @"\blocal\s+function\s+\w+",
        RegexOptions.Compiled);

    /// <summary>
    /// Lua require 模块加载模式。
    /// </summary>
    private static readonly Regex _rxLuaRequire = new(
        @"\brequire\s*[\('""]",
        RegexOptions.Compiled);

    /// <summary>
    /// Lua then 关键字模式，六种语言中仅 Lua 使用 then。
    /// </summary>
    private static readonly Regex _rxLuaThen = new(
        @"\bthen\b",
        RegexOptions.Compiled);

    // ── Nim ──

    /// <summary>
    /// Nim proc 定义模式（含可选返回类型与等号）。
    /// </summary>
    private static readonly Regex _rxNimProcDef = new(
        @"\bproc\s+\w+\*?\s*\([^)]*\)\s*(?::\s*\w+)?\s*=",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Nim 标准库模块导入模式（strutils、sequtils 等 Python 中不存在的模块名）。
    /// </summary>
    private static readonly Regex _rxNimStdImport = new(
        @"\bimport\s+(?:strutils|sequtils|tables|sets|parseopt|strformat|sugar|asyncdispatch|asyncnet|asyncfutures|httpclient|htmlparser|xmlparser|parseutils|pegs|nre|options)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Nim echo 语句模式（行首）。
    /// </summary>
    private static readonly Regex _rxNimEcho = new(
        @"^\s*echo\s+",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Nim proc 关键字存在性检测。
    /// </summary>
    private static readonly Regex _rxNimProcAny = new(
        @"\bproc\s+\w+",
        RegexOptions.Compiled);

    // ── Go ──

    /// <summary>
    /// Go package 声明模式。
    /// </summary>
    private static readonly Regex _rxGoPackage = new(
        @"^package\s+\w+\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Go func main() { 模式。
    /// </summary>
    private static readonly Regex _rxGoFuncMain = new(
        @"^func\s+main\s*\(\s*\)\s*\{",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Go fmt 包导入模式。
    /// </summary>
    private static readonly Regex _rxGoFmtImport = new(
        @"import\s+(?:\(\s*)?""fmt""",
        RegexOptions.Compiled);

    /// <summary>
    /// Go 短变量声明模式 :=。
    /// </summary>
    private static readonly Regex _rxGoShortDecl = new(
        @":=",
        RegexOptions.Compiled);

    /// <summary>
    /// Go func 声明模式。
    /// </summary>
    private static readonly Regex _rxGoFuncDecl = new(
        @"\bfunc\s+\w+\s*\(",
        RegexOptions.Compiled);

    #endregion

    #region 确定性标记检查函数

    /// <summary>
    /// 各语言的确定性标记检查函数，命中则认为可确定语言。
    /// </summary>
    /// <remarks>
    /// 每个函数由一组预编译正则表达式组合而成，满足任一子条件即返回 true。
    /// 组合条件（AND）用于需要多个信号共同确认的场景，降低误判率。
    /// </remarks>
    private static readonly Dictionary<string, Func<string, bool>> _definitiveMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["python"] = code =>
            _rxPyNameMain.IsMatch(code)
            || _rxPyDefColon.IsMatch(code)
            || _rxPyClassColon.IsMatch(code)
            || _rxPyFromDotImport.IsMatch(code)
            || _rxPyDunder.IsMatch(code),

        ["powershell"] = code =>
            _rxPsCmdletBinding.IsMatch(code)
            || _rxPsParameter.IsMatch(code)
            || _rxPsBuiltinVar.IsMatch(code)
            || (_rxPsDollarAssign.IsMatch(code) && _rxPsCmdlet.IsMatch(code)),

        ["bat"] = code =>
            _rxBatEchoOff.IsMatch(code)
            || _rxBatSetLocalDelayed.IsMatch(code)
            || _rxBatForVar.IsMatch(code)
            || (_rxBatVarRef.IsMatch(code) && (_rxBatGoto.IsMatch(code)
                || code.Contains("setlocal", StringComparison.OrdinalIgnoreCase))),

        ["lua"] = code =>
            _rxLuaLocalFunc.IsMatch(code)
            || _rxLuaRequire.IsMatch(code)
            || (code.Contains("~=") && (_rxLuaThen.IsMatch(code)
                || code.Contains("local ", StringComparison.Ordinal))),

        ["nim"] = code =>
            _rxNimProcDef.IsMatch(code)
            || _rxNimStdImport.IsMatch(code)
            || (_rxNimEcho.IsMatch(code) && _rxNimProcAny.IsMatch(code)),

        ["go"] = code =>
            _rxGoPackage.IsMatch(code)
            || _rxGoFuncMain.IsMatch(code)
            || _rxGoFmtImport.IsMatch(code)
            || (_rxGoShortDecl.IsMatch(code) && _rxGoFuncDecl.IsMatch(code)),
    };

    #endregion

    #region 强特征正则表达式

    /// <summary>
    /// 各语言的强特征正则表达式集合，优先选择在六种目标语言中具有排他性的模式。
    /// </summary>
    /// <remarks>
    /// 设计原则：每个模式在 {bat, powershell, python, lua, nim, go} 中尽量仅匹配目标语言。
    /// 少量共享模式（如 import）保留以提供辅助信号，但排他性模式占主导权重。
    /// </remarks>
    private static readonly Dictionary<string, Regex[]> _strongFeatures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bat"] =
        [
            new Regex(@"^\s*set\s+\w+=", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase),
            new Regex(@"\bif\s+(?:not\s+)?(?:exist|defined|errorlevel)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"^\s*for\s+%%\w+\s+in\s+\(", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase),
            new Regex(@"^\s*goto\s+:\w+", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase),
            new Regex(@"^\s*:\w+\s*$", RegexOptions.Compiled | RegexOptions.Multiline),
            new Regex(@"%[A-Za-z_]\w*%", RegexOptions.Compiled),
            new Regex(@"\bsetlocal\b|\bendlocal\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"\bcall\s+:\w+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ],
        ["powershell"] =
        [
            new Regex(@"\$[A-Za-z_]\w*\s*=", RegexOptions.Compiled),
            new Regex(@"\b(?:Get|Set|New|Remove|Add|Import|Export|Invoke|Start|Stop|Test|Write|Read)-[A-Z]\w+", RegexOptions.Compiled),
            new Regex(@"\|\s*(?:Where-Object|ForEach-Object|Select-Object|Sort-Object|Group-Object|Measure-Object)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"\bparam\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"-(?:eq|ne|gt|lt|ge|le|like|match|contains|in|notin)\b", RegexOptions.Compiled),
            new Regex(@"\$(?:true|false|null)\b", RegexOptions.Compiled),
            new Regex(@"\$_\b", RegexOptions.Compiled),
            new Regex(@"@\{|@\(", RegexOptions.Compiled),
        ],
        ["python"] =
        [
            new Regex(@"^\s*def\s+\w+\s*\([^)]*\)\s*:", RegexOptions.Compiled | RegexOptions.Multiline),
            new Regex(@"^\s*class\s+\w+[^{]*:", RegexOptions.Compiled | RegexOptions.Multiline),
            new Regex(@"\bself\.\w+", RegexOptions.Compiled),
            new Regex(@"__\w+__", RegexOptions.Compiled),
            new Regex(@"\bf[""']", RegexOptions.Compiled),
            new Regex(@"\blambda\s+[^:]+:", RegexOptions.Compiled),
            new Regex(@"\bwith\s+\S+.*\bas\s+\w+\s*:", RegexOptions.Compiled),
            new Regex(@"\[\s*\w+\s+for\s+\w+\s+in\s+", RegexOptions.Compiled),
            new Regex(@"^\s*from\s+[\w.]+\s+import\s+", RegexOptions.Compiled | RegexOptions.Multiline),
            new Regex(@"^\s*import\s+[\w.]+", RegexOptions.Compiled | RegexOptions.Multiline),
        ],
        ["lua"] =
        [
            new Regex(@"\blocal\s+\w+\s*=", RegexOptions.Compiled),
            new Regex(@"\blocal\s+function\s+\w+", RegexOptions.Compiled),
            new Regex(@"\bthen\b", RegexOptions.Compiled),
            new Regex(@"~=", RegexOptions.Compiled),
            new Regex(@"\brequire\s*[\('""]", RegexOptions.Compiled),
            new Regex(@"\b(?:pairs|ipairs)\s*\(", RegexOptions.Compiled),
            new Regex(@"\b(?:table|string|math|io)\.\w+", RegexOptions.Compiled),
            new Regex(@"\brepeat\b", RegexOptions.Compiled),
        ],
        ["nim"] =
        [
            new Regex(@"\bproc\s+\w+\s*\([^)]*\)", RegexOptions.Compiled),
            new Regex(@"\bvar\s+\w+\s*:\s*[A-Z]\w*", RegexOptions.Compiled),
            new Regex(@"\blet\s+\w+\s*=", RegexOptions.Compiled),
            new Regex(@"\bdiscard\b", RegexOptions.Compiled),
            new Regex(@"\b(?:method|iterator|template)\s+\w+", RegexOptions.Compiled),
            new Regex(@"\btype\s+\w+\s*\*?\s*=\s*(?:object|ref|enum|distinct)\b", RegexOptions.Compiled),
            new Regex(@"\bimport\s+\w+", RegexOptions.Compiled),
            new Regex(@"^\s*echo\s+", RegexOptions.Compiled | RegexOptions.Multiline),
        ],
        ["go"] =
        [
            new Regex(@"\bfunc\s+\w+\s*\(", RegexOptions.Compiled),
            new Regex(@"\bpackage\s+\w+", RegexOptions.Compiled),
            new Regex(@":=", RegexOptions.Compiled),
            new Regex(@"\bimport\s+(?:\(\s*)?""", RegexOptions.Compiled),
            new Regex(@"\bfmt\.(?:Print|Println|Printf|Sprintf|Fprintf|Errorf)\b", RegexOptions.Compiled),
            new Regex(@"\bgo\s+\w+\(", RegexOptions.Compiled),
            new Regex(@"\b(?:chan|select)\s+", RegexOptions.Compiled),
            new Regex(@"\bmake\s*\(", RegexOptions.Compiled),
            new Regex(@"\b(?:struct|interface)\s*\{", RegexOptions.Compiled),
            new Regex(@"\bfunc\s*\(\s*\w+\s+\*?\w+\s*\)", RegexOptions.Compiled),
        ],
    };

    #endregion

    #region 弱特征关键字

    /// <summary>
    /// 各语言的弱特征关键字集合，提供辅助评分信号。
    /// </summary>
    private static readonly Dictionary<string, string[]> _weakFeatures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bat"] = ["echo", "pause", "exit", "call", "start", "copy", "move", "del", "mkdir", "rmdir", "cls", "title", "verify"],
        ["powershell"] = ["Write-Host", "Write-Output", "$true", "$false", "$null", "ForEach-Object", "Where-Object", "-ErrorAction", "-Verbose"],
        ["python"] = ["print", "elif", "except", "finally", "lambda", "yield", "assert", "pass", "raise", "nonlocal", "global"],
        ["lua"] = ["nil", "elseif", "repeat", "until", "pairs", "ipairs", "table", "string", "math", "tostring", "tonumber"],
        ["nim"] = ["nil", "discard", "proc", "echo", "addr", "cast", "converter", "distinct", "iterator", "method"],
        ["go"] = ["nil", "make", "append", "len", "cap", "range", "chan", "select", "goroutine", "struct", "interface", "fallthrough"],
    };

    #endregion

    #region 公开 API

    /// <summary>
    /// 对代码进行语言检测，返回按置信度降序排列的检测结果列表。
    /// </summary>
    /// <param name="code">待检测的代码字符串，允许为 null 或空字符串。</param>
    /// <returns>
    /// 包含所有支持语言及其置信度的检测结果列表，按置信度从高到低排序。
    /// 若输入为 null 或空白字符串，返回所有语言置信度为 0 的结果列表。
    /// </returns>
    /// <remarks>
    /// 检测分四层执行：Shebang 检查、确定性标记（命中即返回高分）、强特征组合（累积计分）、弱特征兜底（辅助参考）。
    /// 当多个语言同时命中确定性标记时回退到特征评分，避免误判。
    /// </remarks>
    public static IReadOnlyList<DetectionResult> Detect(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Config.SupportedLanguages
                .Select(lang => new DetectionResult(lang, 0.0))
                .ToList();
        }

        string? definitiveLanguage = CheckDefinitiveMarkers(code);
        if (definitiveLanguage is not null)
        {
            return BuildDefinitiveResults(definitiveLanguage);
        }

        Dictionary<string, double> scores = CalculateFeatureScores(code);

        return Config.SupportedLanguages
            .Select(lang => new DetectionResult(lang, scores.GetValueOrDefault(lang, 0.0)))
            .OrderByDescending(r => r.Confidence)
            .ThenBy(r => r.Language, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 获取代码最可能的语言检测结果。
    /// </summary>
    /// <param name="code">待检测的代码字符串，允许为 null 或空字符串。</param>
    /// <returns>置信度最高的检测结果；若输入为 null 或空白字符串，返回首个支持语言且置信度为 0 的结果。</returns>
    public static DetectionResult DetectTop(string? code)
    {
        return Detect(code).First();
    }

    /// <summary>
    /// 获取代码的前 N 个最可能的语言检测结果。
    /// </summary>
    /// <param name="code">待检测的代码字符串，允许为 null 或空字符串。</param>
    /// <param name="count">返回结果的数量，必须大于 0。</param>
    /// <returns>置信度最高的前 N 个检测结果列表，若 count 大于支持的语言数量则返回全部结果。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 count 小于等于 0 时抛出。</exception>
    public static IReadOnlyList<DetectionResult> DetectTopN(string? code, int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, Text.Localize("结果数量必须大于 0。"));
        }

        return Detect(code).Take(count).ToList();
    }

    #endregion

    #region 确定性标记检查

    /// <summary>
    /// 检查代码是否包含确定性标记，依次检查 Shebang 和各语言标记。
    /// </summary>
    /// <param name="code">待检查的代码字符串，已验证非空。</param>
    /// <returns>
    /// 若唯一一个语言的确定性标记命中则返回该语言标识符；
    /// 若无命中或多个语言同时命中（冲突）则返回 null，回退到特征评分。
    /// </returns>
    private static string? CheckDefinitiveMarkers(string code)
    {
        string? shebangLanguage = CheckShebang(code);
        if (shebangLanguage is not null)
        {
            return shebangLanguage;
        }

        List<string> matches = _definitiveMarkers
            .Where(kvp => kvp.Value(code))
            .Select(kvp => kvp.Key)
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// 检查代码首行是否包含 Shebang 声明。
    /// </summary>
    /// <param name="code">待检查的代码字符串，已验证非空。</param>
    /// <returns>若首行包含有效的 Shebang 声明则返回对应的语言标识符，否则返回 null。</returns>
    private static string? CheckShebang(string code)
    {
        int firstLineEnd = code.IndexOf('\n');
        string firstLine = firstLineEnd >= 0 ? code[..firstLineEnd] : code;

        Match match = _shebangRegex.Match(firstLine);
        return match.Success ? _shebangMappings.GetValueOrDefault(match.Groups[1].Value) : null;
    }

    /// <summary>
    /// 构建确定性标记命中时的检测结果列表。
    /// </summary>
    /// <param name="language">命中的语言标识符。</param>
    /// <returns>命中语言置信度为 <see cref="DefinitiveScore"/>，其余语言置信度为 0 的结果列表。</returns>
    private static IReadOnlyList<DetectionResult> BuildDefinitiveResults(string language)
    {
        return Config.SupportedLanguages
            .Select(lang => new DetectionResult(
                lang,
                string.Equals(lang, language, StringComparison.OrdinalIgnoreCase) ? DefinitiveScore : 0.0))
            .OrderByDescending(r => r.Confidence)
            .ThenBy(r => r.Language, StringComparer.Ordinal)
            .ToList();
    }

    #endregion

    #region 特征评分

    /// <summary>
    /// 计算各语言的特征匹配分数。
    /// </summary>
    /// <param name="code">待分析的代码字符串，已验证非空。</param>
    /// <returns>语言标识符到置信度分数的映射字典。</returns>
    private static Dictionary<string, double> CalculateFeatureScores(string code)
    {
        return Config.SupportedLanguages.ToDictionary(
            lang => lang,
            lang => Math.Min(CalculateStrongFeatureScore(code, lang) + CalculateWeakFeatureScore(code, lang), 1.0),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 计算指定语言的强特征匹配分数。
    /// </summary>
    /// <param name="code">待分析的代码字符串。</param>
    /// <param name="language">目标语言标识符。</param>
    /// <returns>强特征匹配的累积分数，上限为 <see cref="StrongFeatureMaxScore"/>。</returns>
    /// <remarks>
    /// 评分曲线：单个匹配即给出 0.32 的基础信号，多个匹配快速提升至上限。
    /// 这确保了具有排他性模式的语言在仅命中一个强特征时也能获得有意义的区分度。
    /// </remarks>
    private static double CalculateStrongFeatureScore(string code, string language)
    {
        if (!_strongFeatures.TryGetValue(language, out Regex[]? patterns))
        {
            return 0.0;
        }

        int matchCount = patterns.Count(pattern => pattern.IsMatch(code));

        double score = matchCount switch
        {
            >= 5 => StrongFeatureMaxScore,
            4 => 0.88,
            3 => 0.76,
            2 => 0.56,
            1 => 0.32,
            _ => 0.0,
        };

        return Math.Min(score, StrongFeatureMaxScore);
    }

    /// <summary>
    /// 计算指定语言的弱特征匹配分数。
    /// </summary>
    /// <param name="code">待分析的代码字符串。</param>
    /// <param name="language">目标语言标识符。</param>
    /// <returns>弱特征匹配的累积分数，上限为 <see cref="WeakFeatureMaxScore"/>。</returns>
    private static double CalculateWeakFeatureScore(string code, string language)
    {
        if (!_weakFeatures.TryGetValue(language, out string[]? keywords))
        {
            return 0.0;
        }

        int matchCount = keywords.Count(keyword => code.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        return Math.Min(matchCount * WeakFeatureScore, WeakFeatureMaxScore);
    }

    #endregion
}