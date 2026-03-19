/*
 * 语法高亮分析器
 * 提供代码文本的语法高亮区间分析，支持多种脚本语言的关键字、字符串、注释、数字识别
 * 支持可选的范围参数以实现视窗局部高亮
 *
 * @author: WaterRun
 * @file: Static/Highlight.cs
 * @date: 2026-03-19
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RunOnce.Static;

/// <summary>
/// 高亮 Token 类型枚举，定义语法高亮支持的元素类别。
/// </summary>
public enum TokenType
{
    /// <summary>语言关键字，如 if、for、function 等。</summary>
    Keyword,

    /// <summary>字符串字面量，包括单引号和双引号字符串。</summary>
    String,

    /// <summary>注释，包括单行注释和多行注释。</summary>
    Comment,

    /// <summary>数字字面量，包括整数和浮点数。</summary>
    Number,
}

/// <summary>
/// 高亮区间记录，承载单个语法元素的位置与类型信息。
/// </summary>
/// <remarks>
/// 不变量：Start 必须非负；Length 必须为正数；Start + Length 不超过源代码长度。
/// 线程安全：作为不可变记录类型，天然线程安全。
/// 副作用：无。
/// </remarks>
/// <param name="Start">区间起始位置，基于字符索引，从 0 开始。</param>
/// <param name="Length">区间长度，必须大于 0。</param>
/// <param name="Type">Token 类型，决定该区间应使用的颜色类别。</param>
public readonly record struct HighlightSpan(int Start, int Length, TokenType Type)
{
    /// <summary>
    /// 获取区间的结束位置（不包含）。
    /// </summary>
    /// <value>Start + Length 的计算结果。</value>
    public int End => Start + Length;

    /// <summary>
    /// 判断当前区间是否与另一区间重叠。
    /// </summary>
    /// <param name="other">待比较的另一区间。</param>
    /// <returns>若两区间有交集则返回 true，否则返回 false。</returns>
    public bool Overlaps(HighlightSpan other) => Start < other.End && End > other.Start;
}

/// <summary>
/// 语法高亮分析器静态类，提供代码文本的语法高亮区间分析功能。
/// </summary>
/// <remarks>
/// 不变量：所有高亮规则为硬编码且不可变；返回的区间列表不包含重叠区间。
/// 线程安全：所有公开方法为线程安全，内部状态均为只读。
/// 副作用：无。
///
/// 分析按固定优先级分四阶段执行：注释 → 字符串 → 数字 → 关键字。
/// 前序阶段产生的区间通过字符级占用位图屏蔽后序阶段，确保高优先级 Token 不被低优先级覆盖。
///
/// 支持可选的视窗范围参数（rangeStart, rangeEnd）。当指定范围时，仅返回与该范围重叠的 span，
/// 但分析仍基于完整代码文本以保证跨行注释/字符串的准确性。内部通过限制搜索范围和提前终止
/// 来减少不必要的计算量。
/// </remarks>
public static class Highlight
{
    #region 语言配置数据

    /// <summary>
    /// 各语言的关键字集合，键为语言标识符，值为关键字数组。
    /// </summary>
    private static readonly Dictionary<string, string[]> _keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bat"] =
        [
            "call", "cd", "chdir", "cls", "cmd", "color", "copy", "del", "dir", "echo", "else",
            "endlocal", "equ", "errorlevel", "exist", "exit", "for", "geq", "goto", "gtr", "if",
            "in", "leq", "lss", "md", "mkdir", "move", "neq", "not", "nul", "path", "pause", "popd",
            "pushd", "rd", "rem", "ren", "rename", "rmdir", "set", "setlocal", "shift", "start",
            "title", "type", "ver", "verify", "vol"
        ],
        ["powershell"] =
        [
            "Begin", "Break", "Catch", "Class", "Continue", "Data", "Define", "Do", "DynamicParam",
            "Else", "ElseIf", "End", "Exit", "Filter", "Finally", "For", "ForEach", "From", "Function",
            "If", "In", "Param", "Process", "Return", "Switch", "Throw", "Trap", "Try", "Until",
            "Using", "While", "Workflow"
        ],
        ["python"] =
        [
            "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class",
            "continue", "def", "del", "elif", "else", "except", "finally", "for", "from", "global",
            "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass", "raise",
            "return", "try", "while", "with", "yield"
        ],
        ["lua"] =
        [
            "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto",
            "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"
        ],
        ["nim"] =
        [
            "addr", "and", "as", "asm", "bind", "block", "break", "case", "cast", "concept", "const",
            "continue", "converter", "defer", "discard", "distinct", "div", "do", "elif", "else",
            "end", "enum", "except", "export", "finally", "for", "from", "func", "if", "import", "in",
            "include", "interface", "is", "isnot", "iterator", "let", "macro", "method", "mixin",
            "mod", "nil", "not", "notin", "object", "of", "or", "out", "proc", "ptr", "raise", "ref",
            "return", "shl", "shr", "static", "template", "try", "tuple", "type", "using", "var",
            "when", "while", "xor", "yield"
        ],
        ["go"] =
        [
            "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough",
            "for", "func", "go", "goto", "if", "import", "interface", "map", "package", "range",
            "return", "select", "struct", "switch", "type", "var"
        ],
    };

    /// <summary>
    /// 各语言的注释模式配置，包含单行注释前缀和多行注释分隔符。
    /// </summary>
    private static readonly Dictionary<string, CommentPattern> _commentPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bat"] = new CommentPattern(["REM ", "rem ", "::"], null, null),
        ["powershell"] = new CommentPattern(["#"], "<#", "#>"),
        ["python"] = new CommentPattern(["#"], null, null),
        ["lua"] = new CommentPattern(["--"], "--[[", "]]"),
        ["nim"] = new CommentPattern(["#"], "#[", "]#"),
        ["go"] = new CommentPattern(["//"], "/*", "*/"),
    };

    /// <summary>
    /// 各语言的字符串分隔符配置。
    /// </summary>
    private static readonly Dictionary<string, StringPattern> _stringPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bat"] = new StringPattern(['"'], false, null),
        ["powershell"] = new StringPattern(['"', '\''], false, ["@\"", "\"@", "@'", "'@"]),
        ["python"] = new StringPattern(['"', '\''], true, ["\"\"\"", "\"\"\"", "'''", "'''"]),
        ["lua"] = new StringPattern(['"', '\''], true, ["[[", "]]"]),
        ["nim"] = new StringPattern(['"'], true, ["\"\"\""]),
        ["go"] = new StringPattern(['"', '\'', '`'], true, null),
    };

    /// <summary>
    /// 匹配数字字面量的正则表达式，支持整数、浮点数、十六进制、科学计数法。
    /// </summary>
    private static readonly Regex _numberRegex = new(
        @"\b(?:0[xX][0-9a-fA-F]+|0[bB][01]+|0[oO][0-7]+|\d+\.?\d*(?:[eE][+-]?\d+)?|\.\d+(?:[eE][+-]?\d+)?)\b",
        RegexOptions.Compiled);

    #endregion

    #region 公开 API

    /// <summary>
    /// 对代码进行语法高亮分析，返回高亮区间列表。
    /// </summary>
    /// <param name="code">待分析的代码字符串，允许为 null 或空字符串。</param>
    /// <param name="language">脚本语言标识符，必须是支持的语言之一，允许为 null 或空字符串。</param>
    /// <param name="rangeStart">分析范围的起始字符索引（包含），为 null 时分析整个文本。</param>
    /// <param name="rangeEnd">分析范围的结束字符索引（不包含），为 null 时分析整个文本。</param>
    /// <returns>
    /// 按起始位置升序排列的高亮区间列表，区间之间不重叠。
    /// 当指定范围时，仅返回与 [rangeStart, rangeEnd) 重叠的区间。
    /// 若输入代码为空或语言不支持，返回空列表。
    /// </returns>
    public static IReadOnlyList<HighlightSpan> Analyze(string? code, string? language, int? rangeStart = null, int? rangeEnd = null)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrWhiteSpace(language))
        {
            return [];
        }

        if (!_keywords.ContainsKey(language))
        {
            return [];
        }

        int effectiveRangeStart = rangeStart.HasValue ? Math.Max(0, rangeStart.Value) : 0;
        int effectiveRangeEnd = rangeEnd.HasValue ? Math.Min(code.Length, rangeEnd.Value) : code.Length;

        if (effectiveRangeStart >= effectiveRangeEnd)
        {
            return [];
        }

        List<HighlightSpan> spans = [];
        OccupancyMap occupied = new(code.Length);

        AnalyzeComments(code, language, spans, occupied, effectiveRangeStart, effectiveRangeEnd);
        AnalyzeStrings(code, language, spans, occupied, effectiveRangeStart, effectiveRangeEnd);
        AnalyzeNumbers(code, spans, occupied, effectiveRangeStart, effectiveRangeEnd);
        AnalyzeKeywords(code, language, spans, occupied, effectiveRangeStart, effectiveRangeEnd);

        spans.Sort(static (a, b) =>
        {
            int cmp = a.Start.CompareTo(b.Start);
            return cmp != 0 ? cmp : a.Length.CompareTo(b.Length);
        });

        return spans;
    }

    #endregion

    #region 注释分析

    /// <summary>
    /// 分析代码中的注释并添加到高亮区间列表。
    /// </summary>
    /// <param name="code">待分析的完整代码字符串。</param>
    /// <param name="language">脚本语言标识符。</param>
    /// <param name="spans">高亮区间列表，分析结果将追加到此列表。</param>
    /// <param name="occupied">字符级占用位图，用于避免与已识别 Token 重叠。</param>
    /// <param name="rangeStart">视窗范围起始位置。</param>
    /// <param name="rangeEnd">视窗范围结束位置。</param>
    private static void AnalyzeComments(string code, string language, List<HighlightSpan> spans, OccupancyMap occupied, int rangeStart, int rangeEnd)
    {
        if (!_commentPatterns.TryGetValue(language, out CommentPattern pattern))
        {
            return;
        }

        if (pattern.MultiLineStart is not null && pattern.MultiLineEnd is not null)
        {
            int searchStart = 0;
            while (searchStart < code.Length)
            {
                int startIndex = code.IndexOf(pattern.MultiLineStart, searchStart, StringComparison.Ordinal);
                if (startIndex < 0)
                {
                    break;
                }

                int endIndex = code.IndexOf(pattern.MultiLineEnd, startIndex + pattern.MultiLineStart.Length, StringComparison.Ordinal);
                int spanEnd = endIndex >= 0 ? endIndex + pattern.MultiLineEnd.Length : code.Length;

                if (spanEnd > rangeStart && startIndex < rangeEnd)
                {
                    TryAddSpan(spans, occupied, startIndex, spanEnd, TokenType.Comment);
                }
                else if (startIndex < rangeEnd)
                {
                    occupied.Mark(startIndex, spanEnd);
                }

                searchStart = spanEnd;

                if (startIndex >= rangeEnd && endIndex >= 0)
                {
                    break;
                }
            }
        }

        foreach (string prefix in pattern.SingleLinePrefixes)
        {
            int searchStart = Math.Max(0, rangeStart - 500);
            while (searchStart < rangeEnd)
            {
                int startIndex = code.IndexOf(prefix, searchStart, StringComparison.OrdinalIgnoreCase);
                if (startIndex < 0 || startIndex >= rangeEnd)
                {
                    break;
                }

                int lineEnd = code.IndexOf('\n', startIndex);
                int spanEnd = lineEnd >= 0 ? lineEnd : code.Length;

                if (spanEnd > rangeStart)
                {
                    TryAddSpan(spans, occupied, startIndex, spanEnd, TokenType.Comment);
                }

                searchStart = spanEnd + 1;
            }
        }
    }

    #endregion

    #region 字符串分析

    /// <summary>
    /// 分析代码中的字符串字面量并添加到高亮区间列表。
    /// </summary>
    /// <param name="code">待分析的完整代码字符串。</param>
    /// <param name="language">脚本语言标识符。</param>
    /// <param name="spans">高亮区间列表，分析结果将追加到此列表。</param>
    /// <param name="occupied">字符级占用位图，用于避免与已识别 Token 重叠。</param>
    /// <param name="rangeStart">视窗范围起始位置。</param>
    /// <param name="rangeEnd">视窗范围结束位置。</param>
    private static void AnalyzeStrings(string code, string language, List<HighlightSpan> spans, OccupancyMap occupied, int rangeStart, int rangeEnd)
    {
        if (!_stringPatterns.TryGetValue(language, out StringPattern pattern))
        {
            return;
        }

        if (pattern.MultiLineDelimiters is not null)
        {
            for (int i = 0; i < pattern.MultiLineDelimiters.Length; i += 2)
            {
                string startDelim = pattern.MultiLineDelimiters[i];
                string endDelim = i + 1 < pattern.MultiLineDelimiters.Length ? pattern.MultiLineDelimiters[i + 1] : startDelim;

                int searchStart = 0;
                while (searchStart < code.Length)
                {
                    int startIndex = code.IndexOf(startDelim, searchStart, StringComparison.Ordinal);
                    if (startIndex < 0)
                    {
                        break;
                    }

                    if (occupied.IsOccupied(startIndex, startIndex + 1))
                    {
                        searchStart = startIndex + 1;
                        continue;
                    }

                    int endIndex = code.IndexOf(endDelim, startIndex + startDelim.Length, StringComparison.Ordinal);
                    int spanEnd = endIndex >= 0 ? endIndex + endDelim.Length : code.Length;

                    if (spanEnd > rangeStart && startIndex < rangeEnd)
                    {
                        spans.Add(new HighlightSpan(startIndex, spanEnd - startIndex, TokenType.String));
                        occupied.Mark(startIndex, spanEnd);
                    }
                    else if (startIndex < rangeEnd)
                    {
                        occupied.Mark(startIndex, spanEnd);
                    }

                    searchStart = spanEnd;

                    if (startIndex >= rangeEnd && endIndex >= 0)
                    {
                        break;
                    }
                }
            }
        }

        foreach (char delimiter in pattern.Delimiters)
        {
            int searchStart = Math.Max(0, rangeStart - 500);
            while (searchStart < rangeEnd)
            {
                int startIndex = code.IndexOf(delimiter, searchStart);
                if (startIndex < 0 || startIndex >= rangeEnd)
                {
                    break;
                }

                if (occupied.IsOccupied(startIndex, startIndex + 1))
                {
                    searchStart = startIndex + 1;
                    continue;
                }

                int spanEnd = FindStringEnd(code, startIndex, delimiter, pattern.SupportsEscape);

                if (spanEnd > rangeStart)
                {
                    spans.Add(new HighlightSpan(startIndex, spanEnd - startIndex, TokenType.String));
                    occupied.Mark(startIndex, spanEnd);
                }

                searchStart = spanEnd;
            }
        }
    }

    /// <summary>
    /// 查找字符串字面量的结束位置。
    /// </summary>
    /// <param name="code">代码字符串。</param>
    /// <param name="startIndex">字符串起始位置（包含开始分隔符）。</param>
    /// <param name="delimiter">字符串分隔符字符。</param>
    /// <param name="supportsEscape">是否支持反斜杠转义。</param>
    /// <returns>字符串结束位置（不包含），若未找到结束分隔符则返回代码末尾或行尾。</returns>
    private static int FindStringEnd(string code, int startIndex, char delimiter, bool supportsEscape)
    {
        int current = startIndex + 1;
        while (current < code.Length)
        {
            char c = code[current];

            if (c == '\n' && delimiter != '`')
            {
                return current;
            }

            if (supportsEscape && c == '\\' && current + 1 < code.Length)
            {
                current += 2;
                continue;
            }

            if (c == delimiter)
            {
                return current + 1;
            }

            current++;
        }

        return code.Length;
    }

    #endregion

    #region 数字分析

    /// <summary>
    /// 分析代码中的数字字面量并添加到高亮区间列表。
    /// </summary>
    /// <param name="code">待分析的完整代码字符串。</param>
    /// <param name="spans">高亮区间列表，分析结果将追加到此列表。</param>
    /// <param name="occupied">字符级占用位图，用于避免与已识别 Token 重叠。</param>
    /// <param name="rangeStart">视窗范围起始位置。</param>
    /// <param name="rangeEnd">视窗范围结束位置。</param>
    private static void AnalyzeNumbers(string code, List<HighlightSpan> spans, OccupancyMap occupied, int rangeStart, int rangeEnd)
    {
        int searchFrom = Math.Max(0, rangeStart - 50);
        int searchTo = Math.Min(code.Length, rangeEnd + 50);
        string segment = code[searchFrom..searchTo];

        foreach (Match match in _numberRegex.Matches(segment))
        {
            int absoluteStart = searchFrom + match.Index;
            int absoluteEnd = absoluteStart + match.Length;

            if (absoluteEnd > rangeStart && absoluteStart < rangeEnd)
            {
                TryAddSpan(spans, occupied, absoluteStart, absoluteEnd, TokenType.Number);
            }
        }
    }

    #endregion

    #region 关键字分析

    /// <summary>
    /// 分析代码中的关键字并添加到高亮区间列表。
    /// </summary>
    /// <param name="code">待分析的完整代码字符串。</param>
    /// <param name="language">脚本语言标识符。</param>
    /// <param name="spans">高亮区间列表，分析结果将追加到此列表。</param>
    /// <param name="occupied">字符级占用位图，用于避免与已识别 Token 重叠。</param>
    /// <param name="rangeStart">视窗范围起始位置。</param>
    /// <param name="rangeEnd">视窗范围结束位置。</param>
    private static void AnalyzeKeywords(string code, string language, List<HighlightSpan> spans, OccupancyMap occupied, int rangeStart, int rangeEnd)
    {
        if (!_keywords.TryGetValue(language, out string[]? keywords))
        {
            return;
        }

        bool isCaseSensitive = language is not ("bat" or "powershell");
        StringComparison comparison = isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        int searchFrom = Math.Max(0, rangeStart - 50);

        foreach (string keyword in keywords)
        {
            int searchStart = searchFrom;
            while (searchStart < rangeEnd)
            {
                int index = code.IndexOf(keyword, searchStart, comparison);
                if (index < 0 || index >= rangeEnd)
                {
                    break;
                }

                bool isWordStart = index == 0 || !IsWordChar(code[index - 1]);
                bool isWordEnd = index + keyword.Length >= code.Length || !IsWordChar(code[index + keyword.Length]);

                if (isWordStart && isWordEnd && index + keyword.Length > rangeStart)
                {
                    TryAddSpan(spans, occupied, index, index + keyword.Length, TokenType.Keyword);
                }

                searchStart = index + 1;
            }
        }
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 尝试将指定区间作为高亮 Token 添加到结果列表，仅在区间未被占用时生效。
    /// </summary>
    /// <param name="spans">高亮区间列表。</param>
    /// <param name="occupied">字符级占用位图。</param>
    /// <param name="start">区间起始位置。</param>
    /// <param name="end">区间结束位置（不包含）。</param>
    /// <param name="type">Token 类型。</param>
    /// <returns>若成功添加则返回 true，区间被占用则返回 false。</returns>
    private static bool TryAddSpan(List<HighlightSpan> spans, OccupancyMap occupied, int start, int end, TokenType type)
    {
        if (occupied.IsOccupied(start, end))
        {
            return false;
        }

        spans.Add(new HighlightSpan(start, end - start, type));
        occupied.Mark(start, end);
        return true;
    }

    /// <summary>
    /// 判断字符是否为单词组成字符（字母、数字或下划线）。
    /// </summary>
    /// <param name="c">待判断的字符。</param>
    /// <returns>若为单词字符则返回 true，否则返回 false。</returns>
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    #endregion

    #region 私有类型

    /// <summary>
    /// 字符级占用位图，以 O(区间长度) 复杂度判定任意区间是否已被高优先级 Token 占据。
    /// </summary>
    private readonly struct OccupancyMap
    {
        private readonly bool[] _bits;

        public OccupancyMap(int length)
        {
            _bits = new bool[length];
        }

        public bool IsOccupied(int start, int end)
        {
            int clampedEnd = Math.Min(end, _bits.Length);
            for (int i = Math.Max(0, start); i < clampedEnd; i++)
            {
                if (_bits[i])
                {
                    return true;
                }
            }

            return false;
        }

        public void Mark(int start, int end)
        {
            int clampedStart = Math.Max(0, start);
            int clampedEnd = Math.Min(end, _bits.Length);
            if (clampedStart < clampedEnd)
            {
                Array.Fill(_bits, true, clampedStart, clampedEnd - clampedStart);
            }
        }
    }

    /// <summary>
    /// 注释模式配置记录，定义语言的注释语法规则。
    /// </summary>
    private readonly record struct CommentPattern(string[] SingleLinePrefixes, string? MultiLineStart, string? MultiLineEnd);

    /// <summary>
    /// 字符串模式配置记录，定义语言的字符串字面量语法规则。
    /// </summary>
    private readonly record struct StringPattern(char[] Delimiters, bool SupportsEscape, string[]? MultiLineDelimiters);

    #endregion
}