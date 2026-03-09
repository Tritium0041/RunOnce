# TestCode

测试用例, 用于验证各语言的"一次运行"功能.

---

## bat

```bat
@echo off
chcp 65001 >nul
echo ===== 当前目录文件统计 =====
set fileCount=0
set dirCount=0
for /f %%a in ('dir /a-d /b 2^>nul ^| find /c /v ""') do set fileCount=%%a
for /f %%a in ('dir /ad /b 2^>nul ^| find /c /v ""') do set dirCount=%%a
echo 当前目录: %cd%
echo 文件数量: %fileCount%
echo 文件夹数量: %dirCount%
echo.
echo ===== 文件列表 =====
for %%f in (*.*) do (
    echo [文件] %%f  -  %%~zf 字节
)
for /d %%d in (*) do (
    echo [目录] %%d
)
pause
```

```bat
@echo off
chcp 65001 >nul
echo ===== 系统信息摘要 =====
echo.
echo 计算机名: %COMPUTERNAME%
echo 用户名:   %USERNAME%
echo 系统目录: %SystemRoot%
echo.
echo --- CPU 信息 ---
wmic cpu get Name /value 2>nul | findstr /r /v "^$"
echo.
echo --- 内存信息 ---
for /f "skip=1 tokens=*" %%a in ('wmic os get TotalVisibleMemorySize /value 2^>nul') do (
    for /f "tokens=2 delims==" %%b in ("%%a") do (
        set /a memMB=%%b/1024
        echo 总物理内存: 约 !memMB! MB
    )
)
setlocal enabledelayedexpansion
for /f "skip=1 tokens=*" %%a in ('wmic os get TotalVisibleMemorySize /value 2^>nul') do (
    for /f "tokens=2 delims==" %%b in ("%%a") do (
        set /a memMB=%%b/1024
        echo 总物理内存: 约 !memMB! MB
    )
)
endlocal
echo.
echo --- 磁盘使用 ---
for /f "skip=1 tokens=1-3" %%a in ('wmic logicaldisk where "DriveType=3" get DeviceID^,Size^,FreeSpace /format:table 2^>nul') do (
    if not "%%a"=="" echo 驱动器 %%b  空闲: %%a  总计: %%c
)
echo.
echo --- IP 地址 ---
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /i "IPv4"') do echo IPv4:%%a
pause
```

```bat
@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
echo ===== 查找大文件 (当前目录及子目录) =====
set threshold=1048576
set count=0
echo.
echo 大于 1MB 的文件:
echo ----------------------------------------
for /r %%f in (*) do (
    if %%~zf gtr %threshold% (
        set /a count+=1
        set "size=%%~zf"
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

---

## powershell

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

```powershell
Write-Host "===== 重复文件查找器 =====" -ForegroundColor Cyan
Write-Host "扫描当前目录及子目录..." -ForegroundColor Yellow
Write-Host ""

$allFiles = Get-ChildItem -Path . -Recurse -File -ErrorAction SilentlyContinue
$hashGroups = @{}

$i = 0
foreach ($file in $allFiles) {
    $i++
    Write-Progress -Activity "计算文件哈希" -Status $file.Name -PercentComplete (($i / $allFiles.Count) * 100)
    try {
        $hash = (Get-FileHash -Path $file.FullName -Algorithm MD5).Hash
        if (-not $hashGroups.ContainsKey($hash)) {
            $hashGroups[$hash] = @()
        }
        $hashGroups[$hash] += $file
    } catch {
        # 跳过无法读取的文件
    }
}
Write-Progress -Activity "计算文件哈希" -Completed

$duplicates = $hashGroups.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 }
$dupCount = 0

foreach ($dup in $duplicates) {
    $dupCount++
    $size = $dup.Value[0].Length
    Write-Host "--- 重复组 #$dupCount (大小: $size 字节) ---" -ForegroundColor Red
    foreach ($f in $dup.Value) {
        Write-Host "  $($f.FullName)"
    }
    Write-Host ""
}

if ($dupCount -eq 0) {
    Write-Host "未发现重复文件." -ForegroundColor Green
} else {
    $wastedBytes = ($duplicates | ForEach-Object { $_.Value[0].Length * ($_.Value.Count - 1) } | Measure-Object -Sum).Sum
    Write-Host "共发现 $dupCount 组重复文件, 浪费空间约 $([math]::Round($wastedBytes / 1KB, 2)) KB" -ForegroundColor Yellow
}

Read-Host "按回车键退出"
```

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
    @{Port=8443; Name="HTTPS-Alt"},
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

---

## python

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
        print(f"{ext:<15} {info['count']:>6} {info['size']/1024:>10.1f} KB")

    # 找出最大的5个文件
    print(f"\n最大的 5 个文件:")
    all_files = [(f, f.stat().st_size) for f in p.rglob("*") if f.is_file()]
    all_files.sort(key=lambda x: x[1], reverse=True)
    for f, size in all_files[:5]:
        print(f"  {size/1024:>10.1f} KB  {f.relative_to(p)}")

scan_directory()
input("\n按回车键退出...")
```

```python
import os
import hashlib
from pathlib import Path
from datetime import datetime

def find_duplicates(directory="."):
    """查找重复文件"""
    print("===== 重复文件查找器 =====\n")
    print(f"扫描目录: {Path(directory).resolve()}\n")

    size_map = {}
    for filepath in Path(directory).rglob("*"):
        if filepath.is_file():
            size = filepath.stat().st_size
            size_map.setdefault(size, []).append(filepath)

    # 只对大小相同的文件计算哈希
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

```python
import os
import json
from pathlib import Path
from datetime import datetime

def generate_tree(directory=".", max_depth=3):
    """生成目录树并导出为 JSON"""
    root = Path(directory).resolve()
    print(f"===== 目录树生成器 =====")
    print(f"根目录: {root}")
    print(f"最大深度: {max_depth}\n")

    def build_tree(path, depth=0):
        if depth >= max_depth:
            return None
        node = {
            "name": path.name or str(path),
            "type": "directory",
            "children": []
        }
        try:
            items = sorted(path.iterdir(), key=lambda x: (x.is_file(), x.name.lower()))
        except PermissionError:
            node["error"] = "权限不足"
            return node

        for item in items:
            if item.name.startswith("."):
                continue
            if item.is_dir():
                child = build_tree(item, depth + 1)
                if child:
                    node["children"].append(child)
            else:
                node["children"].append({
                    "name": item.name,
                    "type": "file",
                    "size": item.stat().st_size
                })
        return node

    def print_tree(node, prefix="", is_last=True):
        connector = "└── " if is_last else "├── "
        if prefix:
            line = prefix + connector + node["name"]
        else:
            line = node["name"]

        if node["type"] == "file":
            size = node.get("size", 0)
            if size > 1024 * 1024:
                line += f"  ({size/1024/1024:.1f} MB)"
            elif size > 1024:
                line += f"  ({size/1024:.1f} KB)"
            else:
                line += f"  ({size} B)"
        print(line)

        if "children" in node:
            children = node["children"]
            for i, child in enumerate(children):
                ext = "    " if is_last else "│   "
                new_prefix = prefix + ext if prefix else ext
                print_tree(child, new_prefix if prefix else "    " if is_last else "│   ", i == len(children) - 1)

    tree = build_tree(root)

    # 打印树
    print_tree(tree)

    # 导出 JSON
    output_file = "directory_tree.json"
    with open(output_file, "w", encoding="utf-8") as f:
        json.dump(tree, f, ensure_ascii=False, indent=2)
    print(f"\n已导出到 {output_file}")

    # 统计
    def count_nodes(node):
        files, dirs = 0, 0
        if node["type"] == "file":
            files = 1
        else:
            dirs = 1
            for child in node.get("children", []):
                f, d = count_nodes(child)
                files += f
                dirs += d
        return files, dirs

    f_count, d_count = count_nodes(tree)
    print(f"共 {d_count} 个目录, {f_count} 个文件")

generate_tree()
input("\n按回车键退出...")
```

```python
"""
此脚本需要在"确保兼容"模式才能运行
"""

from PIL import Image
import os


def generate_asset(source_img, target_width, target_height, output_path, padding_ratio=0.0):
    """
    将源图等比缩放到目标尺寸内（不压缩变形），居中放置在透明画布上。
    padding_ratio: 内边距比例，0.0 表示铺满，0.1 表示四周留 10% 空白。
    """
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
    print(f"  ✔ {os.path.basename(output_path):55s}  {target_width}×{target_height}")


def main():
    assets_dir = os.path.dirname(os.path.abspath(__file__))
    source_path = os.path.join(assets_dir, "logo.png")

    if not os.path.exists(source_path):
        print(f"❌ 找不到源文件: {source_path}")
        return

    source = Image.open(source_path).convert("RGBA")
    print(f"源图: {source_path}  ({source.size[0]}×{source.size[1]})\n")

    # (文件名, 宽, 高)
    assets = [
        # LockScreenLogo  (基准 24×24)
        ("LockScreenLogo.scale-100.png",  24,   24),
        ("LockScreenLogo.scale-200.png",  48,   48),

        # SplashScreen  (基准 620×300)
        ("SplashScreen.scale-100.png",   620,  300),
        ("SplashScreen.scale-150.png",   930,  450),
        ("SplashScreen.scale-200.png",  1240,  600),
        ("SplashScreen.scale-400.png",  2480, 1200),

        # Square150x150Logo  (基准 150×150)
        ("Square150x150Logo.scale-100.png", 150, 150),
        ("Square150x150Logo.scale-150.png", 225, 225),
        ("Square150x150Logo.scale-200.png", 300, 300),
        ("Square150x150Logo.scale-400.png", 600, 600),

        # Square44x44Logo  (基准 44×44)
        ("Square44x44Logo.scale-100.png",  44,  44),
        ("Square44x44Logo.scale-150.png",  66,  66),
        ("Square44x44Logo.scale-200.png",  88,  88),
        ("Square44x44Logo.scale-400.png", 176, 176),

        # Square44x44Logo targetsize altform-unplated
        ("Square44x44Logo.targetsize-16_altform-unplated.png",   16,  16),
        ("Square44x44Logo.targetsize-24_altform-unplated.png",   24,  24),
        ("Square44x44Logo.targetsize-32_altform-unplated.png",   32,  32),
        ("Square44x44Logo.targetsize-48_altform-unplated.png",   48,  48),
        ("Square44x44Logo.targetsize-256_altform-unplated.png", 256, 256),

        # StoreLogo  (基准 50×50)
        ("StoreLogo.png", 50, 50),

        # Wide310x150Logo  (基准 310×150)
        ("Wide310x150Logo.scale-100.png",  310,  150),
        ("Wide310x150Logo.scale-150.png",  465,  225),
        ("Wide310x150Logo.scale-200.png",  620,  300),
        ("Wide310x150Logo.scale-400.png", 1240,  600),
    ]

    for filename, w, h in assets:
        output_path = os.path.join(assets_dir, filename)
        generate_asset(source, w, h, output_path)

    print(f"\n✅ 完成，共生成 {len(assets)} 张资源图。")


if __name__ == "__main__":
    main()
```

---

## lua

```lua
local lfs_ok, lfs = pcall(require, "lfs")

print("===== 当前目录文件统计 =====\n")

local dir = lfs_ok and lfs.currentdir() or "."
print("当前目录: " .. dir)

local file_count = 0
local dir_count = 0
local total_size = 0
local ext_stats = {}
local files_list = {}

if lfs_ok then
    for entry in lfs.dir(dir) do
        if entry ~= "." and entry ~= ".." then
            local path = dir .. "/" .. entry
            local attr = lfs.attributes(path)
            if attr then
                if attr.mode == "file" then
                    file_count = file_count + 1
                    total_size = total_size + attr.size
                    local ext = entry:match("%.([^%.]+)$") or "(无扩展名)"
                    ext = ext:lower()
                    if not ext_stats[ext] then
                        ext_stats[ext] = {count = 0, size = 0}
                    end
                    ext_stats[ext].count = ext_stats[ext].count + 1
                    ext_stats[ext].size = ext_stats[ext].size + attr.size
                    table.insert(files_list, {name = entry, size = attr.size, time = attr.modification})
                elseif attr.mode == "directory" then
                    dir_count = dir_count + 1
                end
            end
        end
    end
else
    -- 无 lfs 时使用 io.popen
    local handle = io.popen('dir /b /a-d 2>nul')
    if handle then
        for line in handle:lines() do
            file_count = file_count + 1
            local ext = line:match("%.([^%.]+)$") or "(无)"
            if not ext_stats[ext] then ext_stats[ext] = {count = 0, size = 0} end
            ext_stats[ext].count = ext_stats[ext].count + 1
        end
        handle:close()
    end
    local dhandle = io.popen('dir /b /ad 2>nul')
    if dhandle then
        for line in dhandle:lines() do dir_count = dir_count + 1 end
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

```lua
print("===== 文本文件搜索工具 =====\n")

io.write("输入搜索关键词: ")
local keyword = io.read("*l")
if not keyword or keyword == "" then
    keyword = "TODO"
    print("使用默认关键词: " .. keyword)
end

local extensions = {".txt", ".lua", ".py", ".md", ".json", ".xml", ".csv", ".log", ".bat", ".ps1"}
local ext_set = {}
for _, e in ipairs(extensions) do ext_set[e] = true end

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
                content = line:sub(1, 120)
            })
        end
    end
    f:close()
end

-- 使用系统命令获取文件列表
local cmd
if package.config:sub(1,1) == "\\" then
    cmd = 'dir /b /s /a-d 2>nul'
else
    cmd = 'find . -type f 2>/dev/null'
end

local handle = io.popen(cmd)
if handle then
    for filepath in handle:lines() do
        local ext = filepath:match("(%.[^%.\\]+)$")
        if ext and ext_set[ext:lower()] then
            search_file(filepath)
        end
    end
    handle:close()
end

print(string.format("\n搜索完成: 扫描 %d 个文件, %d 行", files_scanned, lines_scanned))
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
                -- 加粗和斜体
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

-- 读取当前目录的所有 .md 文件并转换
local cmd = package.config:sub(1,1) == "\\" and 'dir /b *.md 2>nul' or 'ls *.md 2>/dev/null'
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
    f:write(demo)
    f:close()
    print("已生成 demo.md, 请重新运行.")
else
    print(string.format("\n共转换 %d 个文件", converted))
end

io.read()
```

---

## nim

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
      let ext = if entry.splitFile().ext != "": entry.splitFile().ext.toLowerAscii() else: "(无扩展名)"
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
  let hExt   = "扩展名"
  let hCount = "数量"
  let hSize  = "大小"
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

```nim
import os, strutils, strformat, times, algorithm

type FileEntry = tuple[path: string, size: int64, modified: Time]

proc findLargeFiles(dir: string = ".", thresholdKB: int = 100) =
  echo "===== 大文件查找器 ====="
  echo fmt"目录: {absolutePath(dir)}"
  echo fmt"阈值: {thresholdKB} KB"
  echo ""

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
          except:
            inc errors
        of pcDir:
          walk(entry)
        else: discard
    except:
      inc errors

  walk(dir)

  allFiles.sort(proc(a, b: FileEntry): int = cmp(b.size, a.size))

  echo fmt"扫描文件数: {scanned}"
  echo fmt"总大小: {totalSize.float / 1024.0 / 1024.0:.2f} MB"
  echo fmt"错误: {errors}"
  echo fmt"大于 {thresholdKB}KB 的文件: {allFiles.len} 个"
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

findLargeFiles()
discard readLine(stdin)
```

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
    ".rs": "Rust", ".java": "Java", ".sh": "Shell"
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
    except:
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

  let hLang    = "语言"
  let hFiles   = "文件"
  let hLines   = "总行数"
  let hCode    = "代码"
  let hComment = "注释"
  let hBlank   = "空行"
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
    totalLines   += r.stat.lines
    totalCode    += r.stat.code
    totalComment += r.stat.comment
    totalBlank   += r.stat.blank

  echo "-".repeat(60)
  let hTotal = "合计"
  echo fmt"{hTotal:<15} {totalFiles:>6} {totalLines:>8} {totalCode:>8} {totalComment:>8} {totalBlank:>8}"

countLines()
discard readLine(stdin)
```

---

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
                ext = "(无扩展名)"
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

    // 按大小分组
    sizeMap := make(map[int64][]string)
    filepath.Walk(dir, func(path string, info os.FileInfo, err error) error {
        if err == nil && !info.IsDir() {
            sizeMap[info.Size()] = append(sizeMap[info.Size()], path)
        }
        return nil
    })

    // 对大小相同的文件计算MD5
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

    // 输出重复文件
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

```go
package main

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

    dir, _ := os.Getwd()
    fmt.Printf("目录: %s\n", dir)

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

    filepath.Walk(dir, func(path string, info os.FileInfo, err error) error {
        if err != nil || info.IsDir() {
            if info != nil && info.IsDir() && strings.HasPrefix(info.Name(), ".") && path != dir {
                return filepath.SkipDir
            }
            return nil
        }
        ext := strings.ToLower(filepath.Ext(info.Name()))
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
