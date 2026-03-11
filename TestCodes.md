# TestCodes

RunOnce 测试用例集, 用于验证各语言的一次性脚本执行功能.

标注 **`[命令行参数]`** 的用例需要通过 `Ctrl+E` 设置参数后再执行, 参数示例在脚本头部注释中给出.
标注 **`[确保兼容]`** 的用例需要在设置中将脚本放置行为切换为"确保兼容"模式.

---

## bat

当前目录文件统计

```bat
@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
echo ===== 当前目录文件统计 =====
echo.

set fileCount=0
set dirCount=0
for /f %%a in ('dir /a-d /b 2^>nul ^| find /c /v ""') do set fileCount=%%a
for /f %%a in ('dir /ad /b 2^>nul ^| find /c /v ""') do set dirCount=%%a
echo 当前目录: %cd%
echo 文件数量: !fileCount!
echo 文件夹数量: !dirCount!
echo.
echo ===== 文件列表 =====
for %%f in (*.*) do (
    echo [文件] %%~nxf  -  %%~zf 字节
)
for /d %%d in (*) do (
    echo [目录] %%d
)
echo.
echo ===== 统计完成 =====
endlocal
pause
```

查找大文件 (当前目录及子目录, 大于 1MB)

```bat
@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
echo ===== 查找大文件 (大于 1MB) =====
echo.

set threshold=1048576
set count=0

echo 扫描目录: %cd%
echo ----------------------------------------
for /r %%f in (*) do (
    if %%~zf gtr !threshold! (
        set /a count+=1
        set /a sizeMB=%%~zf/1048576
        echo [!sizeMB! MB]  %%f
    )
)
echo ----------------------------------------
if !count! equ 0 (
    echo 未找到大于 1MB 的文件.
) else (
    echo 共找到 !count! 个大文件.
)
endlocal
pause
```

`[命令行参数]` 指定目录文件列表 — 参数示例: `C:\Windows\Fonts`

```bat
@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

REM 命令行参数: 目标目录路径
REM Ctrl+E 参数示例: C:\Windows\Fonts

set "targetDir=%~1"
if "!targetDir!"=="" (
    echo 用法: 请通过 Ctrl+E 设置命令行参数为目标目录路径
    echo 示例: C:\Windows\Fonts
    pause
    goto :eof
)

if not exist "!targetDir!\" (
    echo 错误: 目录不存在 - !targetDir!
    pause
    goto :eof
)

echo ===== 目录文件列表 =====
echo 目标目录: !targetDir!
echo.

set fileCount=0
for %%f in ("!targetDir!\*.*") do (
    set /a fileCount+=1
    echo [%%~zf 字节]  %%~nxf
)

echo.
echo ----------------------------------------
echo 文件总数: !fileCount!
endlocal
pause
```

---

## powershell

文件统计与按扩展名分组

```powershell
Write-Host "===== 当前目录文件统计 =====" -ForegroundColor Cyan
$location = Get-Location
$items = Get-ChildItem -Path $location
$files = $items | Where-Object { -not $_.PSIsContainer }
$dirs = $items | Where-Object { $_.PSIsContainer }
$totalSize = ($files | Measure-Object -Property Length -Sum).Sum

Write-Host "当前目录: $location"
Write-Host "文件数量: $($files.Count)"
Write-Host "文件夹数量: $($dirs.Count)"
Write-Host ("总大小: {0:N2} MB" -f ($totalSize / 1MB))
Write-Host ""

Write-Host "===== 按扩展名分组 =====" -ForegroundColor Cyan
$files | Group-Object Extension | Sort-Object Count -Descending | ForEach-Object {
    $ext = if ($_.Name -eq "") { "(无扩展名)" } else { $_.Name }
    $groupSize = ($_.Group | Measure-Object -Property Length -Sum).Sum
    Write-Host ("{0,-15} {1,5} 个文件  {2,10:N2} KB" -f $ext, $_.Count, ($groupSize / 1KB))
}

Write-Host ""
Write-Host "===== 最近修改的 10 个文件 =====" -ForegroundColor Cyan
$files | Sort-Object LastWriteTime -Descending | Select-Object -First 10 | ForEach-Object {
    Write-Host ("{0}  {1,12:N0} 字节  {2}" -f $_.LastWriteTime.ToString("yyyy-MM-dd HH:mm"), $_.Length, $_.Name)
}

Read-Host "按回车键退出"
```

本机常用端口扫描

```powershell
Write-Host "===== 端口扫描器 (本机常用端口) =====" -ForegroundColor Cyan
Write-Host ""

$ports = @(
    @{Port=80;   Name="HTTP"},
    @{Port=443;  Name="HTTPS"},
    @{Port=3306; Name="MySQL"},
    @{Port=5432; Name="PostgreSQL"},
    @{Port=6379; Name="Redis"},
    @{Port=8080; Name="HTTP-Alt"},
    @{Port=3389; Name="RDP"},
    @{Port=22;   Name="SSH"},
    @{Port=21;   Name="FTP"},
    @{Port=5000; Name="Dev-Server"},
    @{Port=3000; Name="Node-Dev"},
    @{Port=27017;Name="MongoDB"}
)

$openPorts = @()

foreach ($p in $ports) {
    $tcp = New-Object System.Net.Sockets.TcpClient
    try {
        $result = $tcp.BeginConnect("127.0.0.1", $p.Port, $null, $null)
        $wait = $result.AsyncWaitHandle.WaitOne(300, $false)
        if ($wait -and $tcp.Connected) {
            Write-Host ("[OPEN]   端口 {0,-6} {1}" -f $p.Port, $p.Name) -ForegroundColor Green
            $openPorts += $p
        } else {
            Write-Host ("[CLOSED] 端口 {0,-6} {1}" -f $p.Port, $p.Name) -ForegroundColor DarkGray
        }
    } catch {
        Write-Host ("[CLOSED] 端口 {0,-6} {1}" -f $p.Port, $p.Name) -ForegroundColor DarkGray
    } finally {
        $tcp.Close()
    }
}

Write-Host ""
Write-Host "扫描完成: $($openPorts.Count)/$($ports.Count) 个端口开放" -ForegroundColor Cyan
Read-Host "按回车键退出"
```

`[命令行参数]` 进程信息查看 — 参数示例: `explorer`

```powershell
# 命令行参数: 进程名称关键词
# Ctrl+E 参数示例: explorer

param()

$keyword = if ($args.Count -gt 0) { $args[0] } else { $null }

if (-not $keyword) {
    Write-Host "用法: 请通过 Ctrl+E 设置命令行参数为进程名称关键词" -ForegroundColor Yellow
    Write-Host "示例: explorer"
    Read-Host "按回车键退出"
    exit
}

Write-Host "===== 进程信息查看 =====" -ForegroundColor Cyan
Write-Host "搜索关键词: $keyword" -ForegroundColor Yellow
Write-Host ""

$processes = Get-Process | Where-Object { $_.Name -like "*$keyword*" }

if ($processes.Count -eq 0) {
    Write-Host "未找到匹配的进程." -ForegroundColor Red
} else {
    Write-Host ("{0,-8} {1,-30} {2,12} {3,10}" -f "PID", "进程名", "内存(MB)", "CPU(s)")
    Write-Host ("-" * 65)
    $totalMem = 0
    foreach ($p in $processes | Sort-Object WorkingSet64 -Descending) {
        $memMB = $p.WorkingSet64 / 1MB
        $totalMem += $memMB
        $cpuSec = if ($p.CPU) { "{0:N1}" -f $p.CPU } else { "-" }
        Write-Host ("{0,-8} {1,-30} {2,10:N1} {3,10}" -f $p.Id, $p.Name, $memMB, $cpuSec)
    }
    Write-Host ("-" * 65)
    Write-Host ("匹配进程: {0} 个, 总内存: {1:N1} MB" -f $processes.Count, $totalMem) -ForegroundColor Green
}

Read-Host "按回车键退出"
```

---

## python

目录扫描统计

```python
import os
from pathlib import Path
from collections import defaultdict


def scan_directory(path="."):
    """统计当前目录的文件信息"""
    p = Path(path).resolve()
    print(f"===== 目录扫描: {p} =====\n")

    ext_stats = defaultdict(lambda: {"count": 0, "size": 0})
    total_files = 0
    total_dirs = 0
    total_size = 0

    for item in p.rglob("*"):
        if item.is_file():
            total_files += 1
            size = item.stat().st_size
            total_size += size
            ext = item.suffix.lower() if item.suffix else "(无扩展名)"
            ext_stats[ext]["count"] += 1
            ext_stats[ext]["size"] += size
        elif item.is_dir():
            total_dirs += 1

    print(f"文件总数: {total_files}")
    print(f"目录总数: {total_dirs}")
    print(f"总大小:   {total_size / 1024 / 1024:.2f} MB\n")

    print("按扩展名统计:")
    print(f"{'扩展名':<15} {'数量':>6} {'大小':>12}")
    print("-" * 35)
    for ext, info in sorted(ext_stats.items(), key=lambda x: x[1]["size"], reverse=True):
        print(f"{ext:<15} {info['count']:>6} {info['size'] / 1024:>10.1f} KB")

    print(f"\n最大的 5 个文件:")
    all_files = [(f, f.stat().st_size) for f in p.rglob("*") if f.is_file()]
    all_files.sort(key=lambda x: x[1], reverse=True)
    for f, size in all_files[:5]:
        print(f"  {size / 1024:>10.1f} KB  {f.relative_to(p)}")


scan_directory()
input("\n按回车键退出...")
```

重复文件查找器

```python
import hashlib
from pathlib import Path
from datetime import datetime


def find_duplicates(directory="."):
    """查找重复文件"""
    print("===== 重复文件查找器 =====\n")
    root = Path(directory).resolve()
    print(f"扫描目录: {root}\n")

    size_map = {}
    for filepath in root.rglob("*"):
        if filepath.is_file():
            size = filepath.stat().st_size
            size_map.setdefault(size, []).append(filepath)

    hash_map = {}
    checked = 0
    for size, files in size_map.items():
        if len(files) < 2:
            continue
        for f in files:
            checked += 1
            try:
                h = hashlib.md5(f.read_bytes()).hexdigest()
                hash_map.setdefault(h, []).append(f)
            except (PermissionError, OSError):
                pass

    dup_groups = {h: fs for h, fs in hash_map.items() if len(fs) > 1}
    wasted = 0

    if dup_groups:
        for i, (h, files) in enumerate(dup_groups.items(), 1):
            size = files[0].stat().st_size
            wasted += size * (len(files) - 1)
            print(f"--- 重复组 #{i} (大小: {size:,} 字节, MD5: {h[:12]}...) ---")
            for f in files:
                mtime = datetime.fromtimestamp(f.stat().st_mtime).strftime("%Y-%m-%d %H:%M")
                print(f"  [{mtime}] {f}")
            print()
        print(f"共发现 {len(dup_groups)} 组重复文件")
        print(f"浪费空间: {wasted / 1024:.1f} KB")
    else:
        print("未发现重复文件.")

    print(f"(共检查哈希 {checked} 个文件)")


find_duplicates()
input("\n按回车键退出...")
```

`[命令行参数]` 文本文件统计 — 参数示例: `.py .lua .md`

```python
import sys
import os
from pathlib import Path

# 命令行参数: 一个或多个文件扩展名
# Ctrl+E 参数示例: .py .lua .md


def count_lines(directory, extensions):
    """统计指定扩展名文件的行数信息"""
    root = Path(directory).resolve()
    print(f"===== 文本文件统计 =====")
    print(f"目录: {root}")
    print(f"扩展名: {', '.join(extensions)}\n")

    stats = {}
    for ext in extensions:
        stats[ext] = {"files": 0, "lines": 0, "blank": 0, "chars": 0}

    for filepath in root.rglob("*"):
        if not filepath.is_file():
            continue
        suffix = filepath.suffix.lower()
        if suffix not in stats:
            continue
        try:
            content = filepath.read_text(encoding="utf-8", errors="ignore")
            lines = content.splitlines()
            stats[suffix]["files"] += 1
            stats[suffix]["lines"] += len(lines)
            stats[suffix]["blank"] += sum(1 for line in lines if line.strip() == "")
            stats[suffix]["chars"] += len(content)
        except (PermissionError, OSError):
            pass

    print(f"{'扩展名':<10} {'文件数':>6} {'总行数':>8} {'空行数':>8} {'字符数':>10}")
    print("-" * 48)

    total_files = total_lines = total_blank = total_chars = 0
    for ext in sorted(stats.keys()):
        s = stats[ext]
        print(f"{ext:<10} {s['files']:>6} {s['lines']:>8} {s['blank']:>8} {s['chars']:>10}")
        total_files += s["files"]
        total_lines += s["lines"]
        total_blank += s["blank"]
        total_chars += s["chars"]

    print("-" * 48)
    print(f"{'合计':<10} {total_files:>6} {total_lines:>8} {total_blank:>8} {total_chars:>10}")


if len(sys.argv) < 2:
    print("用法: 请通过 Ctrl+E 设置命令行参数为文件扩展名")
    print("示例: .py .lua .md")
    input("\n按回车键退出...")
    sys.exit(0)

extensions = [ext if ext.startswith(".") else f".{ext}" for ext in sys.argv[1:]]
count_lines(".", extensions)
input("\n按回车键退出...")
```

`[确保兼容]` 批量生成应用资源图 (需要 Pillow 库)

```python
"""
此脚本需要在"确保兼容"模式运行, 因为它需要访问脚本所在目录的 logo.png 文件.
依赖: pip install Pillow
"""

from PIL import Image
import os


def generate_asset(source_img, target_width, target_height, output_path, padding_ratio=0.0):
    """将源图等比缩放到目标尺寸内, 居中放置在透明画布上."""
    canvas = Image.new("RGBA", (target_width, target_height), (0, 0, 0, 0))

    available_w = max(1, int(target_width * (1 - 2 * padding_ratio)))
    available_h = max(1, int(target_height * (1 - 2 * padding_ratio)))

    src_w, src_h = source_img.size
    scale = min(available_w / src_w, available_h / src_h)

    new_w = max(1, int(src_w * scale))
    new_h = max(1, int(src_h * scale))

    resized = source_img.resize((new_w, new_h), Image.LANCZOS)

    x = (target_width - new_w) // 2
    y = (target_height - new_h) // 2

    canvas.paste(resized, (x, y), resized)
    canvas.save(output_path, "PNG")
    print(f"  OK {os.path.basename(output_path):55s}  {target_width}x{target_height}")


def main():
    assets_dir = os.path.dirname(os.path.abspath(__file__))
    source_path = os.path.join(assets_dir, "logo.png")

    if not os.path.exists(source_path):
        print(f"找不到源文件: {source_path}")
        return

    source = Image.open(source_path).convert("RGBA")
    print(f"源图: {source_path}  ({source.size[0]}x{source.size[1]})\n")

    assets = [
        ("LockScreenLogo.scale-100.png", 24, 24),
        ("LockScreenLogo.scale-200.png", 48, 48),
        ("SplashScreen.scale-100.png", 620, 300),
        ("SplashScreen.scale-200.png", 1240, 600),
        ("Square150x150Logo.scale-100.png", 150, 150),
        ("Square150x150Logo.scale-200.png", 300, 300),
        ("Square150x150Logo.scale-400.png", 600, 600),
        ("Square44x44Logo.scale-100.png", 44, 44),
        ("Square44x44Logo.scale-200.png", 88, 88),
        ("Square44x44Logo.targetsize-16_altform-unplated.png", 16, 16),
        ("Square44x44Logo.targetsize-24_altform-unplated.png", 24, 24),
        ("Square44x44Logo.targetsize-32_altform-unplated.png", 32, 32),
        ("Square44x44Logo.targetsize-48_altform-unplated.png", 48, 48),
        ("Square44x44Logo.targetsize-256_altform-unplated.png", 256, 256),
        ("StoreLogo.png", 50, 50),
        ("Wide310x150Logo.scale-100.png", 310, 150),
        ("Wide310x150Logo.scale-200.png", 620, 300),
    ]

    for filename, w, h in assets:
        output_path = os.path.join(assets_dir, filename)
        generate_asset(source, w, h, output_path)

    print(f"\n完成, 共生成 {len(assets)} 张资源图.")


if __name__ == "__main__":
    main()
```

---

## lua

当前目录文件统计

```lua
local lfs_ok, lfs = pcall(require, "lfs")

print("===== 当前目录文件统计 =====\n")

local dir = lfs_ok and lfs.currentdir() or "."
print("当前目录: " .. dir)

local file_count = 0
local dir_count = 0
local total_size = 0
local ext_stats = {}

if lfs_ok then
    for entry in lfs.dir(dir) do
        if entry ~= "." and entry ~= ".." then
            local path = dir .. "/" .. entry
            local attr = lfs.attributes(path)
            if attr then
                if attr.mode == "file" then
                    file_count = file_count + 1
                    total_size = total_size + attr.size
                    local ext = entry:match("%.([^%.]+)$") or "(none)"
                    ext = ext:lower()
                    if not ext_stats[ext] then
                        ext_stats[ext] = {count = 0, size = 0}
                    end
                    ext_stats[ext].count = ext_stats[ext].count + 1
                    ext_stats[ext].size = ext_stats[ext].size + attr.size
                elseif attr.mode == "directory" then
                    dir_count = dir_count + 1
                end
            end
        end
    end
else
    local handle = io.popen("dir /b /a-d 2>nul")
    if handle then
        for line in handle:lines() do
            file_count = file_count + 1
            local ext = line:match("%.([^%.]+)$") or "(none)"
            if not ext_stats[ext] then
                ext_stats[ext] = {count = 0, size = 0}
            end
            ext_stats[ext].count = ext_stats[ext].count + 1
        end
        handle:close()
    end
    local dhandle = io.popen("dir /b /ad 2>nul")
    if dhandle then
        for line in dhandle:lines() do
            dir_count = dir_count + 1
        end
        dhandle:close()
    end
end

print(string.format("文件数量: %d", file_count))
print(string.format("目录数量: %d", dir_count))
print(string.format("总大小:   %.2f KB\n", total_size / 1024))

print("按扩展名统计:")
print(string.format("%-15s %6s %12s", "扩展名", "数量", "大小"))
print(string.rep("-", 35))

local sorted_exts = {}
for ext, info in pairs(ext_stats) do
    table.insert(sorted_exts, {ext = ext, count = info.count, size = info.size})
end
table.sort(sorted_exts, function(a, b) return a.size > b.size end)

for _, item in ipairs(sorted_exts) do
    print(string.format("%-15s %6d %10.1f KB", item.ext, item.count, item.size / 1024))
end

io.read()
```

简易 Markdown 转 HTML

```lua
print("===== 简易 Markdown 转 HTML =====\n")

local function escape_html(s)
    return s:gsub("&", "&"):gsub("<", "<"):gsub(">", ">")
end

local function md_to_html(text)
    local lines = {}
    for line in (text .. "\n"):gmatch("(.-)\n") do
        table.insert(lines, line)
    end

    local html = {}
    local in_code = false
    local in_list = false

    for _, line in ipairs(lines) do
        if line:match("^```") then
            if in_code then
                table.insert(html, "</code></pre>")
                in_code = false
            else
                table.insert(html, "<pre><code>")
                in_code = true
            end
        elseif in_code then
            table.insert(html, escape_html(line))
        elseif line:match("^### (.+)") then
            table.insert(html, "<h3>" .. escape_html(line:match("^### (.+)")) .. "</h3>")
        elseif line:match("^## (.+)") then
            table.insert(html, "<h2>" .. escape_html(line:match("^## (.+)")) .. "</h2>")
        elseif line:match("^# (.+)") then
            table.insert(html, "<h1>" .. escape_html(line:match("^# (.+)")) .. "</h1>")
        elseif line:match("^%- (.+)") then
            if not in_list then
                table.insert(html, "<ul>")
                in_list = true
            end
            table.insert(html, "  <li>" .. escape_html(line:match("^%- (.+)")) .. "</li>")
        else
            if in_list then
                table.insert(html, "</ul>")
                in_list = false
            end
            if line:match("^%s*$") then
                table.insert(html, "")
            else
                local processed = escape_html(line)
                processed = processed:gsub("%*%*(.-)%*%*", "<strong>%1</strong>")
                processed = processed:gsub("%*(.-)%*", "<em>%1</em>")
                table.insert(html, "<p>" .. processed .. "</p>")
            end
        end
    end
    if in_list then table.insert(html, "</ul>") end
    return table.concat(html, "\n")
end

local cmd = package.config:sub(1, 1) == "\\" and "dir /b *.md 2>nul" or "ls *.md 2>/dev/null"
local handle = io.popen(cmd)
local converted = 0

if handle then
    for filename in handle:lines() do
        local f = io.open(filename, "r")
        if f then
            local content = f:read("*a")
            f:close()

            local html_content = [[<!DOCTYPE html>
<html><head><meta charset="utf-8">
<style>body{font-family:sans-serif;max-width:800px;margin:0 auto;padding:20px}
pre{background:#f4f4f4;padding:10px;border-radius:4px;overflow-x:auto}
code{background:#f4f4f4;padding:2px 4px;border-radius:2px}</style>
</head><body>]] .. "\n" .. md_to_html(content) .. "\n</body></html>"

            local out_name = filename:gsub("%.md$", ".html")
            local out = io.open(out_name, "w")
            if out then
                out:write(html_content)
                out:close()
                converted = converted + 1
                print(string.format("转换: %s -> %s (%d 字节)", filename, out_name, #html_content))
            end
        end
    end
    handle:close()
end

if converted == 0 then
    print("当前目录未找到 .md 文件, 生成示例...")
    local demo = "# 示例标题\n\n这是一段**粗体**和*斜体*文字.\n\n## 列表\n\n- 第一项\n- 第二项\n- 第三项\n"
    local f = io.open("demo.md", "w")
    if f then
        f:write(demo)
        f:close()
        print("已生成 demo.md, 请重新运行.")
    end
else
    print(string.format("\n共转换 %d 个文件", converted))
end

io.read()
```

`[命令行参数]` 文本文件搜索 — 参数示例: `TODO`

```lua
-- 命令行参数: 搜索关键词
-- Ctrl+E 参数示例: TODO

local keyword = arg[1]

if not keyword or keyword == "" then
    print("用法: 请通过 Ctrl+E 设置命令行参数为搜索关键词")
    print("示例: TODO")
    io.read()
    os.exit(0)
end

print("===== 文本文件搜索 =====\n")
print("关键词: " .. keyword)
print("")

local extensions = {
    [".txt"] = true, [".lua"] = true, [".py"] = true,
    [".md"] = true, [".json"] = true, [".xml"] = true,
    [".csv"] = true, [".log"] = true, [".bat"] = true,
    [".ps1"] = true, [".go"] = true, [".nim"] = true,
}

local results = {}
local files_scanned = 0
local lines_scanned = 0

local function search_file(filepath)
    local f = io.open(filepath, "r")
    if not f then return end
    files_scanned = files_scanned + 1
    local line_num = 0
    for line in f:lines() do
        line_num = line_num + 1
        lines_scanned = lines_scanned + 1
        if line:find(keyword, 1, true) then
            table.insert(results, {
                file = filepath,
                line = line_num,
                content = line:sub(1, 120),
            })
        end
    end
    f:close()
end

local cmd
if package.config:sub(1, 1) == "\\" then
    cmd = "dir /b /s /a-d 2>nul"
else
    cmd = "find . -type f 2>/dev/null"
end

local handle = io.popen(cmd)
if handle then
    for filepath in handle:lines() do
        local ext = filepath:match("(%.[^%.\\]+)$")
        if ext and extensions[ext:lower()] then
            search_file(filepath)
        end
    end
    handle:close()
end

print(string.format("搜索完成: 扫描 %d 个文件, %d 行", files_scanned, lines_scanned))
print(string.format("找到 %d 处匹配\n", #results))

for i, r in ipairs(results) do
    print(string.format("[%d] %s:%d", i, r.file, r.line))
    print("    " .. r.content:gsub("^%s+", ""))
    if i >= 50 then
        print(string.format("\n... 还有 %d 处匹配未显示", #results - 50))
        break
    end
end

io.read()
```

---

## nim

当前目录文件统计

```nim
import os, strformat, strutils, tables, algorithm, times

proc scanDirectory(path: string = ".") =
  echo "===== 当前目录文件统计 ====="
  echo ""

  let absPath = absolutePath(path)
  echo fmt"目录: {absPath}"
  echo ""

  var
    fileCount = 0
    dirCount = 0
    totalSize: int64 = 0
    extStats = initTable[string, tuple[count: int, size: int64]]()
    filesList: seq[tuple[name: string, size: int64, time: Time]] = @[]

  for kind, entry in walkDir(path):
    case kind
    of pcFile:
      inc fileCount
      let info = getFileInfo(entry)
      let size = info.size
      totalSize += size
      let ext = if entry.splitFile().ext != "": entry.splitFile().ext.toLowerAscii() else: "(none)"
      if ext notin extStats:
        extStats[ext] = (count: 0, size: 0'i64)
      extStats[ext] = (count: extStats[ext].count + 1, size: extStats[ext].size + size)
      filesList.add((name: entry, size: size, time: info.lastWriteTime))
    of pcDir:
      inc dirCount
    else: discard

  echo fmt"文件数量: {fileCount}"
  echo fmt"目录数量: {dirCount}"
  echo fmt"总大小:   {totalSize.float / 1024.0 / 1024.0:.2f} MB"
  echo ""

  echo "按扩展名统计:"
  let hExt = "扩展名"
  let hCount = "数量"
  let hSize = "大小"
  echo fmt"{hExt:<15} {hCount:>6} {hSize:>12}"
  echo "-".repeat(35)

  var sorted: seq[tuple[ext: string, count: int, size: int64]] = @[]
  for ext, info in extStats:
    sorted.add((ext: ext, count: info.count, size: info.size))
  sorted.sort(proc(a, b: auto): int = cmp(b.size, a.size))

  for item in sorted:
    echo fmt"{item.ext:<15} {item.count:>6} {item.size.float / 1024.0:>10.1f} KB"

  echo ""
  echo "最大的 5 个文件:"
  filesList.sort(proc(a, b: auto): int = cmp(b.size, a.size))
  for i in 0 ..< min(5, filesList.len):
    echo fmt"  {filesList[i].size.float / 1024.0:>10.1f} KB  {filesList[i].name}"

scanDirectory()
discard readLine(stdin)
```

代码行数统计

```nim
import os, strutils, strformat, tables, algorithm

proc countLines(dir: string = ".") =
  echo "===== 代码行数统计 ====="
  echo fmt"目录: {absolutePath(dir)}"
  echo ""

  let extMap = {
    ".nim": "Nim", ".py": "Python", ".go": "Go",
    ".lua": "Lua", ".bat": "Batch", ".ps1": "PowerShell",
    ".js": "JavaScript", ".ts": "TypeScript", ".html": "HTML",
    ".css": "CSS", ".json": "JSON", ".md": "Markdown",
    ".c": "C", ".cpp": "C++", ".h": "C Header",
    ".rs": "Rust", ".java": "Java", ".sh": "Shell",
  }.toTable

  type LangStat = object
    files: int
    lines: int
    blank: int
    comment: int
    code: int

  var stats = initTable[string, LangStat]()
  var totalFiles = 0

  proc processFile(path: string) =
    let ext = path.splitFile().ext.toLowerAscii()
    if ext notin extMap: return

    let lang = extMap[ext]
    if lang notin stats:
      stats[lang] = LangStat()

    try:
      let content = readFile(path)
      stats[lang].files += 1
      inc totalFiles
      for line in content.splitLines():
        stats[lang].lines += 1
        let trimmed = line.strip()
        if trimmed.len == 0:
          stats[lang].blank += 1
        elif trimmed.startsWith("#") or trimmed.startsWith("//") or trimmed.startsWith("--"):
          stats[lang].comment += 1
        else:
          stats[lang].code += 1
    except CatchableError:
      discard

  proc walkAll(path: string) =
    for kind, entry in walkDir(path):
      case kind
      of pcFile: processFile(entry)
      of pcDir:
        if not entry.splitPath().tail.startsWith("."):
          walkAll(entry)
      else: discard

  walkAll(dir)

  let hLang = "语言"
  let hFiles = "文件"
  let hLines = "总行数"
  let hCode = "代码"
  let hComment = "注释"
  let hBlank = "空行"
  echo fmt"{hLang:<15} {hFiles:>6} {hLines:>8} {hCode:>8} {hComment:>8} {hBlank:>8}"
  echo "-".repeat(60)

  type Row = tuple[lang: string, stat: LangStat]
  var rows: seq[Row] = @[]
  for lang, stat in stats:
    rows.add((lang: lang, stat: stat))
  rows.sort(proc(a, b: Row): int = cmp(b.stat.code, a.stat.code))

  var totalLines, totalCode, totalComment, totalBlank = 0
  for r in rows:
    echo fmt"{r.lang:<15} {r.stat.files:>6} {r.stat.lines:>8} {r.stat.code:>8} {r.stat.comment:>8} {r.stat.blank:>8}"
    totalLines += r.stat.lines
    totalCode += r.stat.code
    totalComment += r.stat.comment
    totalBlank += r.stat.blank

  echo "-".repeat(60)
  let hTotal = "合计"
  echo fmt"{hTotal:<15} {totalFiles:>6} {totalLines:>8} {totalCode:>8} {totalComment:>8} {totalBlank:>8}"

countLines()
discard readLine(stdin)
```

`[命令行参数]` 大文件查找 — 参数示例: `-- 100`

```nim
import os, strutils, strformat, times, algorithm

# 命令行参数: 大小阈值 (KB)
# Ctrl+E 参数示例: -- 100

proc findLargeFiles(dir: string = ".", thresholdKB: int = 100) =
  echo "===== 大文件查找器 ====="
  echo fmt"目录: {absolutePath(dir)}"
  echo fmt"阈值: {thresholdKB} KB"
  echo ""

  type FileEntry = tuple[path: string, size: int64, modified: Time]

  var
    allFiles: seq[FileEntry] = @[]
    scanned = 0
    errors = 0
    totalSize: int64 = 0

  proc walk(path: string) =
    try:
      for kind, entry in walkDir(path):
        case kind
        of pcFile:
          inc scanned
          try:
            let info = getFileInfo(entry)
            totalSize += info.size
            if info.size >= thresholdKB * 1024:
              allFiles.add((path: entry, size: info.size, modified: info.lastWriteTime))
          except CatchableError:
            inc errors
        of pcDir:
          walk(entry)
        else: discard
    except CatchableError:
      inc errors

  walk(dir)
  allFiles.sort(proc(a, b: FileEntry): int = cmp(b.size, a.size))

  echo fmt"扫描文件数: {scanned}"
  echo fmt"总大小: {totalSize.float / 1024.0 / 1024.0:.2f} MB"
  echo fmt"错误: {errors}"
  echo fmt"大于 {thresholdKB} KB 的文件: {allFiles.len} 个"
  echo ""

  let hSize = "大小"
  let hTime = "修改时间"
  let hPath = "路径"
  echo fmt"{hSize:>12} {hTime:<20} {hPath}"
  echo "-".repeat(70)

  for f in allFiles:
    let sizeStr = if f.size > 1024 * 1024:
        fmt"{f.size.float / 1024.0 / 1024.0:.1f} MB"
      else:
        fmt"{f.size.float / 1024.0:.1f} KB"
    let timeStr = f.modified.format("yyyy-MM-dd HH:mm")
    echo fmt"{sizeStr:>12} {timeStr:<20} {f.path}"

var threshold = 100
if paramCount() >= 1:
  try:
    threshold = parseInt(paramStr(1))
  except ValueError:
    echo "警告: 无效的阈值参数, 使用默认值 100 KB"

findLargeFiles(".", threshold)
discard readLine(stdin)
```

---

## go

当前目录文件统计

```go
package main

import (
    "fmt"
    "os"
    "path/filepath"
    "sort"
    "strings"
)

func main() {
    fmt.Println("===== 当前目录文件统计 =====")
    fmt.Println()

    dir, _ := os.Getwd()
    fmt.Printf("目录: %s\n\n", dir)

    type ExtInfo struct {
        Count int
        Size  int64
    }
    extStats := make(map[string]*ExtInfo)
    var totalFiles, totalDirs int
    var totalSize int64

    type FileEntry struct {
        Path string
        Size int64
    }
    var files []FileEntry

    filepath.Walk(dir, func(path string, info os.FileInfo, err error) error {
        if err != nil || path == dir {
            return nil
        }
        if info.IsDir() {
            totalDirs++
        } else {
            totalFiles++
            totalSize += info.Size()
            ext := strings.ToLower(filepath.Ext(info.Name()))
            if ext == "" {
                ext = "(none)"
            }
            if _, ok := extStats[ext]; !ok {
                extStats[ext] = &ExtInfo{}
            }
            extStats[ext].Count++
            extStats[ext].Size += info.Size()
            files = append(files, FileEntry{Path: path, Size: info.Size()})
        }
        return nil
    })

    fmt.Printf("文件数量: %d\n", totalFiles)
    fmt.Printf("目录数量: %d\n", totalDirs)
    fmt.Printf("总大小:   %.2f MB\n\n", float64(totalSize)/1024/1024)

    fmt.Println("按扩展名统计:")
    fmt.Printf("%-15s %6s %12s\n", "扩展名", "数量", "大小")
    fmt.Println(strings.Repeat("-", 35))

    type ExtRow struct {
        Ext  string
        Info *ExtInfo
    }
    var rows []ExtRow
    for ext, info := range extStats {
        rows = append(rows, ExtRow{ext, info})
    }
    sort.Slice(rows, func(i, j int) bool {
        return rows[i].Info.Size > rows[j].Info.Size
    })
    for _, r := range rows {
        fmt.Printf("%-15s %6d %10.1f KB\n", r.Ext, r.Info.Count, float64(r.Info.Size)/1024)
    }

    fmt.Println("\n最大的 5 个文件:")
    sort.Slice(files, func(i, j int) bool { return files[i].Size > files[j].Size })
    for i := 0; i < len(files) && i < 5; i++ {
        rel, _ := filepath.Rel(dir, files[i].Path)
        fmt.Printf("  %10.1f KB  %s\n", float64(files[i].Size)/1024, rel)
    }

    fmt.Println("\n按回车键退出...")
    fmt.Scanln()
}
```

重复文件查找器

```go
package main

import (
    "crypto/md5"
    "encoding/hex"
    "fmt"
    "io"
    "os"
    "path/filepath"
)

func main() {
    fmt.Println("===== 重复文件查找器 =====")
    fmt.Println()

    dir, _ := os.Getwd()
    fmt.Printf("扫描目录: %s\n\n", dir)

    sizeMap := make(map[int64][]string)
    filepath.Walk(dir, func(path string, info os.FileInfo, err error) error {
        if err == nil && !info.IsDir() {
            sizeMap[info.Size()] = append(sizeMap[info.Size()], path)
        }
        return nil
    })

    hashMap := make(map[string][]string)
    checked := 0
    for _, paths := range sizeMap {
        if len(paths) < 2 {
            continue
        }
        for _, p := range paths {
            checked++
            h, err := hashFile(p)
            if err == nil {
                hashMap[h] = append(hashMap[h], p)
            }
        }
    }

    groupNum := 0
    var wastedBytes int64
    for _, files := range hashMap {
        if len(files) < 2 {
            continue
        }
        groupNum++
        info, _ := os.Stat(files[0])
        size := info.Size()
        wastedBytes += size * int64(len(files)-1)
        fmt.Printf("--- 重复组 #%d (大小: %d 字节) ---\n", groupNum, size)
        for _, f := range files {
            rel, _ := filepath.Rel(dir, f)
            fmt.Printf("  %s\n", rel)
        }
        fmt.Println()
    }

    if groupNum == 0 {
        fmt.Println("未发现重复文件.")
    } else {
        fmt.Printf("共发现 %d 组重复文件\n", groupNum)
        fmt.Printf("浪费空间: %.1f KB\n", float64(wastedBytes)/1024)
    }
    fmt.Printf("(共检查哈希 %d 个文件)\n", checked)

    fmt.Println("\n按回车键退出...")
    fmt.Scanln()
}

func hashFile(path string) (string, error) {
    f, err := os.Open(path)
    if err != nil {
        return "", err
    }
    defer f.Close()
    h := md5.New()
    if _, err := io.Copy(h, f); err != nil {
        return "", err
    }
    return hex.EncodeToString(h.Sum(nil)), nil
}
```

`[命令行参数]` 指定目录代码行数统计 — 参数示例: `.`

```go
package main

// 命令行参数: 目标目录路径
// Ctrl+E 参数示例: .

import (
    "bufio"
    "fmt"
    "os"
    "path/filepath"
    "sort"
    "strings"
    "time"
)

func main() {
    fmt.Println("===== 代码行数统计工具 =====")
    fmt.Println()

    dir := "."
    if len(os.Args) > 1 {
        dir = os.Args[1]
    }

    info, err := os.Stat(dir)
    if err != nil || !info.IsDir() {
        fmt.Printf("错误: 无效的目录路径 - %s\n", dir)
        fmt.Println("用法: 请通过 Ctrl+E 设置命令行参数为目标目录路径")
        fmt.Println("\n按回车键退出...")
        fmt.Scanln()
        return
    }

    absDir, _ := filepath.Abs(dir)
    fmt.Printf("目录: %s\n", absDir)

    langMap := map[string]string{
        ".go": "Go", ".py": "Python", ".js": "JavaScript",
        ".ts": "TypeScript", ".lua": "Lua", ".nim": "Nim",
        ".bat": "Batch", ".ps1": "PowerShell", ".sh": "Shell",
        ".c": "C", ".cpp": "C++", ".h": "C Header",
        ".html": "HTML", ".css": "CSS", ".json": "JSON",
        ".md": "Markdown", ".rs": "Rust", ".java": "Java",
    }

    type LangStat struct {
        Files   int
        Lines   int
        Code    int
        Comment int
        Blank   int
    }
    stats := make(map[string]*LangStat)

    start := time.Now()

    filepath.Walk(dir, func(path string, fi os.FileInfo, err error) error {
        if err != nil || fi.IsDir() {
            if fi != nil && fi.IsDir() && strings.HasPrefix(fi.Name(), ".") && path != dir {
                return filepath.SkipDir
            }
            return nil
        }
        ext := strings.ToLower(filepath.Ext(fi.Name()))
        lang, ok := langMap[ext]
        if !ok {
            return nil
        }
        if stats[lang] == nil {
            stats[lang] = &LangStat{}
        }
        stats[lang].Files++

        f, err := os.Open(path)
        if err != nil {
            return nil
        }
        defer f.Close()

        scanner := bufio.NewScanner(f)
        for scanner.Scan() {
            line := strings.TrimSpace(scanner.Text())
            stats[lang].Lines++
            if line == "" {
                stats[lang].Blank++
            } else if strings.HasPrefix(line, "//") || strings.HasPrefix(line, "#") || strings.HasPrefix(line, "--") {
                stats[lang].Comment++
            } else {
                stats[lang].Code++
            }
        }
        return nil
    })

    elapsed := time.Since(start)

    type Row struct {
        Lang string
        Stat *LangStat
    }
    var rows []Row
    for lang, stat := range stats {
        rows = append(rows, Row{lang, stat})
    }
    sort.Slice(rows, func(i, j int) bool { return rows[i].Stat.Code > rows[j].Stat.Code })

    fmt.Println()
    fmt.Printf("%-15s %6s %8s %8s %8s %8s\n", "语言", "文件", "总行数", "代码", "注释", "空行")
    fmt.Println(strings.Repeat("-", 60))

    var tFiles, tLines, tCode, tComment, tBlank int
    for _, r := range rows {
        s := r.Stat
        fmt.Printf("%-15s %6d %8d %8d %8d %8d\n", r.Lang, s.Files, s.Lines, s.Code, s.Comment, s.Blank)
        tFiles += s.Files
        tLines += s.Lines
        tCode += s.Code
        tComment += s.Comment
        tBlank += s.Blank
    }
    fmt.Println(strings.Repeat("-", 60))
    fmt.Printf("%-15s %6d %8d %8d %8d %8d\n", "合计", tFiles, tLines, tCode, tComment, tBlank)
    fmt.Printf("\n耗时: %v\n", elapsed)

    fmt.Println("\n按回车键退出...")
    fmt.Scanln()
}
```
