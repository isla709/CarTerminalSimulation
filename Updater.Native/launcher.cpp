#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#include <windows.h>
#include <shellapi.h>
#include <objbase.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>

namespace fs = std::filesystem;
static std::wstring quote(const std::wstring& value) {
    std::wstring result = L"\""; unsigned slashes = 0;
    for (wchar_t c : value) {
        if (c == L'\\') { ++slashes; continue; }
        result.append(slashes * (c == L'"' ? 2 : 1), L'\\'); slashes = 0;
        if (c == L'"') result += L'\\';
        result += c;
    }
    result.append(slashes * 2, L'\\'); return result + L'"';
}
static std::wstring readLine(const fs::path& path) {
    std::ifstream stream(path); std::string line; std::getline(stream, line);
    if (!line.empty() && line.back() == '\r') line.pop_back();
    if (line.empty()) return L"";
    int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, line.data(), static_cast<int>(line.size()), nullptr, 0);
    if (count <= 0) throw std::runtime_error("Invalid UTF-8 component path");
    std::wstring result(count, L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, line.data(), static_cast<int>(line.size()), result.data(), count);
    return result;
}
static fs::path child(const fs::path& root, const std::wstring& relative) {
    fs::path rel(relative);
    if (relative.empty() || rel.is_absolute() || relative.find(L':') != std::wstring::npos)
        throw std::runtime_error("Invalid component path");
    for (auto& part : rel) if (part == L".." || part == L".") throw std::runtime_error("Invalid component path");
    return root / rel;
}
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    bool probe = false;
    try {
        wchar_t module[32768]; GetModuleFileNameW(nullptr, module, 32768);
        const fs::path executable(module), root = executable.parent_path();
        int argc = 0; auto argv = CommandLineToArgvW(GetCommandLineW(), &argc);
        std::vector<std::wstring> args; for (int i = 1; i < argc; ++i) args.emplace_back(argv[i]); LocalFree(argv);
#ifdef APP_HOST
        // Keep this process at the original main EXE path. Legacy update plans use
        // Environment.ProcessPath; spawning dotnet.exe would break that contract.
        probe = args.size() == 4 && args[0] == L"--probe";
        const auto runtimeText = probe ? args[2] : readLine(root / L".Updater/app-runtime.txt");
        const auto fxrText = probe ? args[3] : readLine(root / L".Updater/app-hostfxr.txt");
        const auto runtime = fs::path(runtimeText).is_absolute() ? fs::path(runtimeText) : child(root, runtimeText);
        const auto fxr = fs::path(fxrText).is_absolute() ? fs::path(fxrText) : child(root, fxrText);
        if (!fs::exists(fxr) || !fs::exists(runtime)) throw std::runtime_error("Private runtime is unavailable");
        auto library = LoadLibraryExW(fxr.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!library) throw std::runtime_error("Cannot load runtime host");
        struct parameters { size_t size; const wchar_t* host_path; const wchar_t* dotnet_root; };
        using init_fn = int(__cdecl*)(int, const wchar_t**, const parameters*, void**);
        using run_fn = int(__cdecl*)(void*); using close_fn = int(__cdecl*)(void*);
        auto init = reinterpret_cast<init_fn>(GetProcAddress(library, "hostfxr_initialize_for_dotnet_command_line"));
        auto run = reinterpret_cast<run_fn>(GetProcAddress(library, "hostfxr_run_app"));
        auto close = reinterpret_cast<close_fn>(GetProcAddress(library, "hostfxr_close"));
        if (!init || !run || !close) throw std::runtime_error("Runtime host API missing");
        auto dll = probe ? fs::path(args[1]) : executable; dll.replace_extension(L".dll");
        std::wstring managed = dll.wstring(), runtimeRoot = runtime.wstring();
        std::vector<const wchar_t*> managedArgs{ managed.c_str() }; if (!probe) for (auto& arg : args) managedArgs.push_back(arg.c_str());
        parameters config{ sizeof(parameters), module, runtimeRoot.c_str() }; void* context = nullptr;
        CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        int result = init(static_cast<int>(managedArgs.size()), managedArgs.data(), &config, &context);
        if (result < 0 || !context) throw std::runtime_error("Runtime initialization failed");
        if (probe) { close(context); return 0; }
        result = run(context); close(context); return result;
#else
        if (executable.filename() == L"uninstall.exe") {
            args = { L"--uninstall", L"--target", root.wstring() };
        }
        const auto privateRuntime = root / L".Updater" / L"runtimes" / L"net8-x64";
        if (!fs::exists(privateRuntime / L"host" / L"fxr")) throw std::runtime_error("Private updater runtime is unavailable");
        SetEnvironmentVariableW(L"DOTNET_ROOT", privateRuntime.c_str());
        SetEnvironmentVariableW(L"DOTNET_ROOT_X64", privateRuntime.c_str());
        SetEnvironmentVariableW(L"DOTNET_MULTILEVEL_LOOKUP", L"0");
        wchar_t temp[32768]; GetTempPathW(32768, temp);
        auto ready = fs::path(temp) / (L"CTS-updater-ready-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64()));
        SetEnvironmentVariableW(L"CTS_UPDATER_READY", ready.c_str());
        for (const auto* pointer : { L"current.txt", L"last-good.txt" }) {
            auto slot = readLine(root / L".Updater" / pointer);
            if (slot.empty()) continue;
            auto engine = child(root / L".Updater", slot) / L"update.exe";
            if (!fs::exists(engine)) continue;
            SetEnvironmentVariableW(L"CTS_UPDATER_SLOT", slot.c_str());
            auto command = quote(engine.wstring()); for (auto& arg : args) command += L" " + quote(arg);
            STARTUPINFOW si{}; si.cb = sizeof(si); PROCESS_INFORMATION pi{};
            auto job = CreateJobObjectW(nullptr, nullptr);
            if (!job) continue;
            DeleteFileW(ready.c_str());
            if (!CreateProcessW(engine.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_SUSPENDED, nullptr, engine.parent_path().c_str(), &si, &pi)) { CloseHandle(job); continue; }
            if (!AssignProcessToJobObject(job, pi.hProcess)) {
                TerminateProcess(pi.hProcess, 1); CloseHandle(pi.hThread); CloseHandle(pi.hProcess); CloseHandle(job); continue;
            }
            ResumeThread(pi.hThread);
            CloseHandle(pi.hThread);
            // The bootstrap exits after spawning a detached worker. Readiness is
            // acknowledged by that worker only, not by the short-lived parent.
            bool acknowledged = false;
            for (int i = 0; i < 250; ++i) { if (fs::exists(ready)) { acknowledged = true; break; } Sleep(100); }
            CloseHandle(pi.hProcess);
            if (acknowledged) {
                CloseHandle(job); // No kill-on-close: the acknowledged worker continues independently.
                DeleteFileW(ready.c_str());
                if (std::wstring(pointer) == L"last-good.txt") {
                    auto pending = root / L".Updater/current.txt.recovered";
                    { std::ofstream stream(pending); stream << std::string(slot.begin(), slot.end()); }
                    MoveFileExW(pending.c_str(), (root / L".Updater/current.txt").c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
                }
                return 0;
            }
            // A delayed, unhealthy candidate must not wake up after fallback and
            // race the healthy engine. The job contains only this launch tree.
            TerminateJobObject(job, 1);
            CloseHandle(job);
        }
        throw std::runtime_error("No healthy updater engine is available");
#endif
    } catch (...) {
        if (probe) return 1;
        MessageBoxW(nullptr, L"更新兼容组件无法启动。请从完整的 Preview11 或更新发布包修复 .Updater 目录。\n用户配置和工作区不会被修改。", L"启动失败", MB_OK | MB_ICONERROR);
        return 1;
    }
}
