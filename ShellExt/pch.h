/*
 * 预编译头文件
 * 集中引入 Shell Extension 所需的全部 Windows SDK 头文件
 *
 * @file: pch.h
 * @date: 2026-03-06
 */

#pragma once

#include "framework.h"

 // Shell 接口：IExplorerCommand、IShellItem、IShellItemArray 等
#include <shobjidl_core.h>

// Shell 辅助函数：SHStrDupW、PathRemoveFileSpecW、PathAppendW 等
#include <shlwapi.h>

// 安全字符串操作：StringCchCopyW、StringCchPrintfW 等
#include <strsafe.h>

// Shell 执行：SHELLEXECUTEINFOW、ShellExecuteExW
#include <shellapi.h>

// WRL（Windows Runtime C++ Template Library）
#include <wrl/module.h>
#include <wrl/implements.h>
#include <wrl/client.h>

// 链接 Shell 辅助函数库
#pragma comment(lib, "shlwapi.lib")

// 链接 Windows Runtime 基础库（提供 RoOriginateError 等 WRL 内部依赖的符号）
#pragma comment(lib, "runtimeobject.lib")