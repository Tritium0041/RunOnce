/*
 * RunOnce Shell Extension
 * 实现 Windows 11 一级右键菜单项，在文件夹空白处显示 "在此运行代码" / "Run Code Here"
 * 通过 IExplorerCommand COM 接口向文件资源管理器注册上下文菜单命令
 *
 * 工作原理：
 *   1. MSIX 安装时，系统读取 Package.appxmanifest 中的声明，将本 DLL 注册为 COM 服务器
 *   2. 用户在文件夹空白处右键时，Explorer 加载本 DLL 并创建 RunOnceCommand 实例
 *   3. Explorer 调用 GetTitle/GetIcon 获取菜单显示信息
 *   4. 用户点击菜单项后，Explorer 调用 Invoke，本 DLL 启动 RunOnce.exe 并传入文件夹路径
 *   5. MSIX 卸载时，系统自动撤销所有注册
 *
 * @author: WaterRun
 * @file: dllmain.cpp
 * @date: 2026-03-06
 */

#include "pch.h"

using namespace Microsoft::WRL;

// ============================================================================
// 全局状态
// ============================================================================

/// DLL 模块句柄，在 DllMain 中获取。
/// 用于定位 DLL 自身所在目录，进而找到同目录下的 RunOnce.exe 和图标文件。
static HMODULE g_hModule = nullptr;

// ============================================================================
// RunOnceCommand：IExplorerCommand 接口的完整实现
// ============================================================================
//
// __declspec(uuid("..."))：
//   将一个 GUID（全局唯一标识符）绑定到此类。Windows 通过此 GUID 在清单注册和
//   COM 激活中定位到本类。此 GUID 必须与 Package.appxmanifest 中声明的 CLSID 一致。
//
// RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand>：
//   WRL 提供的基类模板，自动实现 COM 的三大基础机制：
//   - AddRef / Release：引用计数，控制对象生命周期
//   - QueryInterface：接口查询，让调用方获取 IExplorerCommand 指针
//   ClassicCom 标志表示这是传统 COM 对象（非 Windows Runtime 对象）。

class __declspec(uuid("D4E5F601-A2B3-4C5D-8E9F-0A1B2C3D4E5F"))
    RunOnceCommand : public RuntimeClass<
    RuntimeClassFlags<ClassicCom>,
    IExplorerCommand>
{
public:

    // ------------------------------------------------------------------------
    // GetTitle：返回菜单项显示的文字
    // ------------------------------------------------------------------------
    /// Explorer 在渲染右键菜单时调用此方法获取菜单文字。
    /// 通过检测系统语言决定显示中文或英文。
    ///
    /// @param psiItemArray  右键操作涉及的 Shell 项目数组（本场景中未使用）。
    /// @param ppszName      [out] 接收菜单文字的宽字符串指针，由 SHStrDupW 分配内存。
    /// @return              S_OK 表示成功。
    IFACEMETHODIMP GetTitle(
        _In_opt_ IShellItemArray* /* psiItemArray */,
        _Outptr_ LPWSTR* ppszName) override
    {
        // 获取当前用户的区域设置名称（如 "zh-CN"、"en-US"）
        WCHAR localeName[LOCALE_NAME_MAX_LENGTH]{};
        GetUserDefaultLocaleName(localeName, LOCALE_NAME_MAX_LENGTH);

        // 以 "zh" 开头即判定为中文环境（涵盖 zh-CN、zh-TW、zh-HK 等）
        const bool isChinese = (wcsncmp(localeName, L"zh", 2) == 0);

        // "在此运行代码" 的 Unicode 转义写法，避免源文件编码问题
        // \x5728=在 \x6B64=此 \x8FD0=运 \x884C=行 \x4EE3=代 \x7801=码
        return SHStrDupW(
            isChinese ? L"\x5728\x6B64\x8FD0\x884C\x4EE3\x7801" : L"Run Code Here",
            ppszName);
    }

    // ------------------------------------------------------------------------
    // GetIcon：返回菜单项图标的文件路径
    // ------------------------------------------------------------------------
    /// Explorer 在渲染右键菜单时调用此方法获取图标。
    /// 图标文件位于 DLL 同级目录的 Assets\logo.ico。
    ///
    /// @param psiItemArray  右键操作涉及的 Shell 项目数组（本场景中未使用）。
    /// @param ppszIcon      [out] 接收图标路径的宽字符串指针。
    /// @return              S_OK 表示成功；E_FAIL 表示无法获取 DLL 路径。
    IFACEMETHODIMP GetIcon(
        _In_opt_ IShellItemArray* /* psiItemArray */,
        _Outptr_ LPWSTR* ppszIcon) override
    {
        WCHAR dllPath[MAX_PATH]{};

        if (!GetModuleFileNameW(g_hModule, dllPath, MAX_PATH))
        {
            *ppszIcon = nullptr;
            return E_FAIL;
        }

        // 移除文件名部分，仅保留目录路径
        PathRemoveFileSpecW(dllPath);

        // 拼接图标相对路径
        PathAppendW(dllPath, L"Assets\\logo.ico");

        return SHStrDupW(dllPath, ppszIcon);
    }

    // ------------------------------------------------------------------------
    // GetToolTip：返回鼠标悬停时的提示文字（不需要）
    // ------------------------------------------------------------------------
    IFACEMETHODIMP GetToolTip(
        _In_opt_ IShellItemArray* /* psiItemArray */,
        _Outptr_ LPWSTR* ppszInfotip) override
    {
        *ppszInfotip = nullptr;
        return E_NOTIMPL;
    }

    // ------------------------------------------------------------------------
    // GetCanonicalName：返回命令的唯一标识 GUID
    // ------------------------------------------------------------------------
    /// Explorer 使用此 GUID 唯一标识本菜单命令。
    IFACEMETHODIMP GetCanonicalName(_Out_ GUID* pguidCommandName) override
    {
        *pguidCommandName = __uuidof(RunOnceCommand);
        return S_OK;
    }

    // ------------------------------------------------------------------------
    // GetState：控制菜单项的可见性和启用状态
    // ------------------------------------------------------------------------
    /// 返回 ECS_ENABLED 表示菜单项始终可见且可点击。
    IFACEMETHODIMP GetState(
        _In_opt_ IShellItemArray* /* psiItemArray */,
        _In_ BOOL /* fOkToBeSlow */,
        _Out_ EXPCMDSTATE* pCmdState) override
    {
        *pCmdState = ECS_ENABLED;
        return S_OK;
    }

    // ------------------------------------------------------------------------
    // GetFlags：返回命令的行为标志
    // ------------------------------------------------------------------------
    IFACEMETHODIMP GetFlags(_Out_ EXPCMDFLAGS* pFlags) override
    {
        *pFlags = ECF_DEFAULT;
        return S_OK;
    }

    // ------------------------------------------------------------------------
    // EnumSubCommands：枚举子菜单命令（本命令无子菜单）
    // ------------------------------------------------------------------------
    IFACEMETHODIMP EnumSubCommands(_Outptr_ IEnumExplorerCommand** ppEnum) override
    {
        *ppEnum = nullptr;
        return E_NOTIMPL;
    }

    // ------------------------------------------------------------------------
    // Invoke：用户点击菜单项后执行的核心逻辑
    // ------------------------------------------------------------------------
    /// 从 Shell 传入的参数中提取当前文件夹路径，然后启动同目录下的 RunOnce.exe，
    /// 将文件夹路径作为命令行参数传入。
    ///
    /// 对于 Directory\Background（文件夹空白处右键），psiItemArray 包含当前文件夹。
    ///
    /// @param psiItemArray  包含右键操作目标的 Shell 项目数组。
    /// @param pbc           绑定上下文（本场景中未使用）。
    /// @return              S_OK 表示成功；E_INVALIDARG 表示参数无效。
    IFACEMETHODIMP Invoke(
        _In_opt_ IShellItemArray* psiItemArray,
        _In_opt_ IBindCtx* /* pbc */) override
    {
        // ── 第 1 步：从 Shell 参数中提取文件夹路径 ──

        WCHAR folderPath[MAX_PATH]{};

        if (psiItemArray)
        {
            DWORD itemCount = 0;
            HRESULT hr = psiItemArray->GetCount(&itemCount);

            if (SUCCEEDED(hr) && itemCount > 0)
            {
                ComPtr<IShellItem> shellItem;
                hr = psiItemArray->GetItemAt(0, &shellItem);

                if (SUCCEEDED(hr))
                {
                    PWSTR rawPath = nullptr;
                    hr = shellItem->GetDisplayName(SIGDN_FILESYSPATH, &rawPath);

                    if (SUCCEEDED(hr) && rawPath)
                    {
                        StringCchCopyW(folderPath, MAX_PATH, rawPath);
                        CoTaskMemFree(rawPath);
                    }
                }
            }
        }

        // 如果无法获取文件夹路径，静默退出
        if (folderPath[0] == L'\0')
        {
            return S_OK;
        }

        // ── 第 2 步：构建 RunOnce.exe 的完整路径 ──

        WCHAR exePath[MAX_PATH]{};

        if (!GetModuleFileNameW(g_hModule, exePath, MAX_PATH))
        {
            return E_FAIL;
        }

        PathRemoveFileSpecW(exePath);
        PathAppendW(exePath, L"RunOnce.exe");

        // ── 第 3 步：构建命令行参数 ──
        // 用引号包裹路径，防止路径中的空格导致参数截断
        // 例如："D:\My Projects\Test"

        WCHAR arguments[MAX_PATH + 8]{};
        StringCchPrintfW(arguments, ARRAYSIZE(arguments), L"\"%s\"", folderPath);

        // ── 第 4 步：启动 RunOnce.exe ──

        SHELLEXECUTEINFOW sei{};
        sei.cbSize = sizeof(sei);
        sei.fMask = 0;                  // 默认行为，无需特殊标志
        sei.lpVerb = L"open";
        sei.lpFile = exePath;
        sei.lpParameters = arguments;
        sei.lpDirectory = folderPath;
        sei.nShow = SW_SHOWNORMAL;

        ShellExecuteExW(&sei);

        return S_OK;
    }
};

// ============================================================================
// COM 类注册
// ============================================================================

/// 向 WRL 的 Module 系统注册 RunOnceCommand 类。
/// 当 Windows 调用 DllGetClassObject 并传入本类的 GUID 时，
/// WRL 会自动创建类工厂并返回 RunOnceCommand 实例。
CoCreatableClass(RunOnceCommand);

// ============================================================================
// DLL 入口点
// ============================================================================

/// DLL 被加载或卸载时由操作系统调用。
/// DLL_PROCESS_ATTACH 时保存模块句柄，供后续定位文件路径使用。
BOOL APIENTRY DllMain(
    HMODULE hModule,
    DWORD   dwReason,
    LPVOID  /* lpReserved */)
{
    if (dwReason == DLL_PROCESS_ATTACH)
    {
        g_hModule = hModule;
        DisableThreadLibraryCalls(hModule);
    }

    return TRUE;
}

// ============================================================================
// COM 导出函数
// ============================================================================

/// COM 类工厂入口点。Windows 通过此函数获取指定 CLSID 的类工厂实例。
/// WRL 的 Module 系统会根据 CoCreatableClass 注册信息自动分发请求。
_Check_return_
STDAPI DllGetClassObject(
    _In_ REFCLSID rclsid,
    _In_ REFIID riid,
    _COM_Outptr_ void** ppv)
{
    return Module<InProc>::GetModule().GetClassObject(rclsid, riid, ppv);
}

/// COM 卸载检查。当不再有活跃的 COM 对象时，返回 S_OK 允许系统卸载本 DLL。
STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().Terminate() ? S_OK : S_FALSE;
}