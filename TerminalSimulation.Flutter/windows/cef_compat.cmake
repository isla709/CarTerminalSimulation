# webview_cef 0.6.2 frees its GPU descriptor from release_callback. Flutter
# 3.38 still reads visible_width/visible_height after invoking that callback.
# Return a separate thread-local descriptor whose lifetime covers PopulateTexture;
# the callback can then release the per-frame texture holder safely. Generate the
# patched source in the build tree so the shared Pub cache remains immutable.
get_target_property(CTS_CEF_SOURCE_DIR webview_cef_plugin SOURCE_DIR)
get_target_property(CTS_CEF_SOURCES webview_cef_plugin SOURCES)
list(FILTER CTS_CEF_SOURCES EXCLUDE REGEX "webview_cef_gpu_texture\\.cc$")
file(READ "${CTS_CEF_SOURCE_DIR}/webview_cef_gpu_texture.cc" CTS_CEF_GPU_SOURCE)
set(CTS_CEF_DESCRIPTOR_RETURN "return &holder->descriptor;")
string(FIND "${CTS_CEF_GPU_SOURCE}" "${CTS_CEF_DESCRIPTOR_RETURN}" CTS_CEF_PATCH_POSITION)
if(CTS_CEF_PATCH_POSITION EQUAL -1)
  message(FATAL_ERROR "Review the CTS CEF descriptor lifetime patch after upgrading webview_cef.")
endif()
string(REPLACE "${CTS_CEF_DESCRIPTOR_RETURN}"
  "thread_local FlutterDesktopGpuSurfaceDescriptor descriptor;\n        descriptor = holder->descriptor;\n        return &descriptor;"
  CTS_CEF_GPU_SOURCE "${CTS_CEF_GPU_SOURCE}")
file(WRITE "${CMAKE_CURRENT_BINARY_DIR}/cts_cef_gpu_texture.cc" "${CTS_CEF_GPU_SOURCE}")
set_property(TARGET webview_cef_plugin PROPERTY SOURCES
  ${CTS_CEF_SOURCES} "${CMAKE_CURRENT_BINARY_DIR}/cts_cef_gpu_texture.cc")
target_include_directories(webview_cef_plugin PRIVATE "${CTS_CEF_SOURCE_DIR}")



# CEF's vblank driver originally starts before CefInitialize, posting tasks to
# a nonexistent UI thread. With some display states WaitForVBlank returns
# immediately, creating a CPU spin and an unbounded warning log.
function(cts_cef_patch VAR OLD NEW)
  string(FIND "${${VAR}}" "${OLD}" POSITION)
  if(POSITION EQUAL -1)
    message(FATAL_ERROR "Review CTS CEF compatibility patch: ${OLD}")
  endif()
  string(REPLACE "${OLD}" "${NEW}" PATCHED "${${VAR}}")
  set(${VAR} "${PATCHED}" PARENT_SCOPE)
endfunction()
file(READ "${CTS_CEF_SOURCE_DIR}/webview_cef_plugin.cpp" CTS_CEF_WINDOWS)
string(REPLACE "\r\n" "\n" CTS_CEF_WINDOWS "${CTS_CEF_WINDOWS}")
# The plugin subclasses the whole Flutter HWND, so its unconditional IME-start
# suppression also swallowed composition in native Flutter editors. CEF owns
# these messages only while an editable DOM node in its focused browser owns IME.
cts_cef_patch(CTS_CEF_WINDOWS
  "LPARAM lparam, UINT_PTR, DWORD_PTR) {\n\t\tswitch (message) {"
  "LPARAM lparam, UINT_PTR, DWORD_PTR) {\n        if (message == WM_IME_SETCONTEXT || message == WM_IME_STARTCOMPOSITION ||\n            message == WM_IME_COMPOSITION || message == WM_IME_ENDCOMPOSITION) {\n            auto owner = webviewPlugins.find(hwnd);\n            if (owner == webviewPlugins.end() || !owner->second->isEditableFocused()) {\n                return DefSubclassProc(hwnd, message, wparam, lparam);\n            }\n        }\n\t\tswitch (message) {")
cts_cef_patch(CTS_CEF_WINDOWS "\t\tStartVsyncDriver();" "\t\t// Start after successful CEF initialization, not plugin registration.")
cts_cef_patch(CTS_CEF_WINDOWS "\t\twhile (g_vsyncRunning) {" "\t\twhile (g_vsyncRunning) {\n            const auto tickStart = std::chrono::steady_clock::now();")
cts_cef_patch(CTS_CEF_WINDOWS "\t\t\tstd::vector<std::shared_ptr<WebviewPlugin>> snapshot;" "            std::this_thread::sleep_until(tickStart + std::chrono::milliseconds(4));\n\t\t\tstd::vector<std::shared_ptr<WebviewPlugin>> snapshot;")
cts_cef_patch(CTS_CEF_WINDOWS "\t\tWValue *encodeArgs =" "#ifdef WEBVIEW_CEF_GPU_TEXTURE\n        const bool initializing = method_call.method_name() == \"init\";\n        if (method_call.method_name() == \"quit\") StopVsyncDriver();\n#endif\n\t\tWValue *encodeArgs =")
cts_cef_patch(CTS_CEF_WINDOWS "\t\t\tif (ret > 0){" "\t\t\tif (ret > 0){\n#ifdef WEBVIEW_CEF_GPU_TEXTURE\n                if (initializing) StartVsyncDriver();\n#endif")
file(WRITE "${CMAKE_CURRENT_BINARY_DIR}/cts_cef_windows.cpp" "${CTS_CEF_WINDOWS}")

file(READ "${CTS_CEF_SOURCE_DIR}/../common/webview_plugin.cc" CTS_CEF_COMMON)
string(REPLACE "\r\n" "\n" CTS_CEF_COMMON "${CTS_CEF_COMMON}")
cts_cef_patch(CTS_CEF_COMMON "CefInitialize(mainArgs, cefs, app.get(), nullptr);" "isCefInitialized = CefInitialize(mainArgs, cefs, app.get(), nullptr);")
cts_cef_patch(CTS_CEF_COMMON "\t\t\t\tstartCEF();" "\t\t\t\tstartCEF();\n                if (!isCefInitialized) { result(-1, nullptr); return; }")
cts_cef_patch(CTS_CEF_COMMON "CefShutdown();" "if (isCefInitialized) { CefShutdown(); isCefInitialized = false; }")
option(CTS_CEF_FILE_LOGGING "Enable native CEF file logging for explicit diagnosis" OFF)
if(NOT CTS_CEF_FILE_LOGGING)
  # Keep diagnostics in CTS's bounded log rather than an unbounded debug.log.
  cts_cef_patch(CTS_CEF_COMMON "CefSettings cefs;" "CefSettings cefs;\n        cefs.log_severity = LOGSEVERITY_DISABLE;")
endif()
file(WRITE "${CMAKE_CURRENT_BINARY_DIR}/cts_cef_common.cc" "${CTS_CEF_COMMON}")
get_target_property(CTS_CEF_SOURCES webview_cef_plugin SOURCES)
list(FILTER CTS_CEF_SOURCES EXCLUDE REGEX "(webview_cef_plugin\\.cpp|webview_plugin\\.cc)$")
set_property(TARGET webview_cef_plugin PROPERTY SOURCES ${CTS_CEF_SOURCES}
  "${CMAKE_CURRENT_BINARY_DIR}/cts_cef_windows.cpp"
  "${CMAKE_CURRENT_BINARY_DIR}/cts_cef_common.cc")
