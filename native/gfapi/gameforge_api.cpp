// NosCat replacement for Gameforge's gameforge_api.dll (x86), used when the Gameforge Client is not installed.
//
// NostaleClientX.exe -> psw_tnt.dll -> gameforge_api.dll. Gameforge's own gameforge_api.dll is only a proxy:
// it looks up HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{<_TNT_CLIENT_APPLICATION_ID or
// 703bd6d7-79c0-4005-8cd7-89522a05a546>}_is1\InstallLocation and loads gameforge_client_api.dll from there;
// without the Gameforge Client that path is "\gameforge_client_api.dll" and the client quits. So instead the
// launcher puts this DLL in its place (keeping the original as gameforge_api.gameforge.dll).
//
// Exports and signatures match the proxy (read from its disassembly and from psw_tnt.dll's imports):
//   bool _gfc_init@0(), bool _gfc_isClientRunning@0(), void _gfc_shutdown@0()
//   bool __stdcall _gfc_queryXxx@8(char* buffer, size_t size)    NUL-terminated string into buffer
//   void __stdcall _gfc_setErrorCallback@8(callback, userData)
//
// Like the real library, the calls that need the launcher go over \\.\pipe\GameforgeClientJSONRPC as JSON-RPC
// (isClientRunning, initSession, queryAuthorizationCode, queryGameAccountName); NosCatLauncher answers them.
// The other queries are answered here from environment variables the launcher sets.
// Every call is logged to %TEMP%\noscat_gfclient.log.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>

namespace {

const wchar_t* const kPipeName = L"\\\\.\\pipe\\GameforgeClientJSONRPC";

std::mutex g_lock;
HANDLE g_pipe = INVALID_HANDLE_VALUE;
int g_nextId = 1;

void Log(const char* format, ...)
{
    char path[MAX_PATH];
    DWORD n = GetTempPathA(MAX_PATH, path);
    if (n == 0 || n > MAX_PATH - 32) return;
    strcat_s(path, "noscat_gfclient.log");

    FILE* f = nullptr;
    if (fopen_s(&f, path, "a") != 0 || f == nullptr) return;
    SYSTEMTIME t;
    GetLocalTime(&t);
    fprintf(f, "[%02d:%02d:%02d.%03d] ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds);
    va_list args;
    va_start(args, format);
    vfprintf(f, format, args);
    va_end(args);
    fputc('\n', f);
    fclose(f);
}

std::string Env(const char* name, const char* fallback)
{
    char buffer[512];
    DWORD n = GetEnvironmentVariableA(name, buffer, sizeof buffer);
    return n > 0 && n < sizeof buffer ? std::string(buffer, n) : std::string(fallback);
}

std::string JsonEscape(const std::string& s)
{
    std::string out;
    for (char c : s)
    {
        if (c == '"' || c == '\\') { out += '\\'; out += c; }
        else if (static_cast<unsigned char>(c) < 0x20) { char u[8]; sprintf_s(u, "\\u%04x", c); out += u; }
        else out += c;
    }
    return out;
}

void ClosePipe()
{
    if (g_pipe != INVALID_HANDLE_VALUE)
    {
        CloseHandle(g_pipe);
        g_pipe = INVALID_HANDLE_VALUE;
    }
}

bool OpenPipe()
{
    if (g_pipe != INVALID_HANDLE_VALUE) return true;
    for (int attempt = 0; attempt < 50; ++attempt)
    {
        g_pipe = CreateFileW(kPipeName, GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (g_pipe != INVALID_HANDLE_VALUE) return true;
        DWORD error = GetLastError();
        if (attempt == 0) Log("pipe not ready (error %lu), retrying", error);
        if (error == ERROR_PIPE_BUSY) WaitNamedPipeW(kPipeName, 200);
        else Sleep(100);
    }
    Log("could not open the pipe; is NosCatLauncher running?");
    return false;
}

// Reads one complete JSON object (braces balanced outside strings) from the pipe.
bool ReadMessage(std::string& message)
{
    message.clear();
    int depth = 0;
    bool inString = false, escaped = false, started = false;
    char c;
    DWORD read;
    while (ReadFile(g_pipe, &c, 1, &read, nullptr) && read == 1)
    {
        if (!started && c != '{') continue;
        started = true;
        message += c;
        if (inString)
        {
            if (escaped) escaped = false;
            else if (c == '\\') escaped = true;
            else if (c == '"') inString = false;
        }
        else if (c == '"') inString = true;
        else if (c == '{') ++depth;
        else if (c == '}' && --depth == 0) return true;
    }
    return false;
}

// Extracts "result" from a JSON-RPC response: a string (unescaped) or a bare literal such as true.
bool ParseResult(const std::string& response, std::string& result)
{
    size_t key = response.find("\"result\"");
    if (key == std::string::npos) return false;
    size_t i = response.find(':', key);
    if (i == std::string::npos) return false;
    ++i;
    while (i < response.size() && (response[i] == ' ' || response[i] == '\t' || response[i] == '\r' || response[i] == '\n')) ++i;
    result.clear();
    if (i < response.size() && response[i] == '"')
    {
        for (++i; i < response.size() && response[i] != '"'; ++i)
        {
            if (response[i] == '\\' && i + 1 < response.size())
            {
                ++i;
                char e = response[i];
                result += e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e;  // \uXXXX is not expected here
            }
            else result += response[i];
        }
        return true;
    }
    while (i < response.size() && response[i] != ',' && response[i] != '}' && response[i] != ' ') result += response[i++];
    return !result.empty() && result != "null";
}

// Sends one JSON-RPC call and waits for its answer; reconnects once if the launcher dropped the pipe.
bool Call(const char* method, std::string& result)
{
    std::lock_guard<std::mutex> guard(g_lock);
    std::string session = JsonEscape(Env("_TNT_SESSION_ID", ""));
    for (int attempt = 0; attempt < 2; ++attempt)
    {
        if (!OpenPipe()) return false;
        int id = g_nextId++;
        std::string request = "{\"id\":" + std::to_string(id) + ",\"jsonrpc\":\"2.0\",\"method\":\"ClientLibrary." + method +
                              "\",\"params\":{\"sessionId\":\"" + session + "\"}}";
        DWORD written;
        std::string response;
        if (WriteFile(g_pipe, request.data(), static_cast<DWORD>(request.size()), &written, nullptr) &&
            written == request.size() && ReadMessage(response))
        {
            bool ok = ParseResult(response, result);
            Log("%s -> %s%s", method, ok ? "" : "(no result) ", response.c_str());
            return ok;
        }
        Log("%s: pipe error %lu, reconnecting", method, GetLastError());
        ClosePipe();
    }
    return false;
}

bool CopyOut(const char* what, const std::string& value, char* buffer, size_t size)
{
    if (buffer == nullptr || size == 0 || value.size() + 1 > size)
    {
        Log("%s: buffer too small (%u for %u)", what, static_cast<unsigned>(size), static_cast<unsigned>(value.size() + 1));
        return false;
    }
    memcpy(buffer, value.c_str(), value.size() + 1);
    return true;
}

bool QueryPipe(const char* method, char* buffer, size_t size)
{
    std::string value;
    return Call(method, value) && CopyOut(method, value, buffer, size);
}

bool QueryLocal(const char* what, const std::string& value, char* buffer, size_t size)
{
    Log("%s -> \"%s\" (local)", what, value.c_str());
    return CopyOut(what, value, buffer, size);
}

std::string Locale() { return Env("NOSCAT_GAME_LOCALE", "en_GB"); }

}  // namespace

// The launcher recognises its own copy by this string, so it can update it but never mistakes Gameforge's.
extern "C" const char noscat_marker[] = "NosCat gameforge_api replacement";

// Exported under the proxy's decorated names by gameforge_api.def.
extern "C" {

bool __stdcall noscat_init()
{
    Log("gfc_init (%s, PID %lu)", noscat_marker, GetCurrentProcessId());
    std::string value;
    if (!Call("isClientRunning", value)) return false;
    Call("initSession", value);
    return true;
}

bool __stdcall noscat_isClientRunning()
{
    std::string value;
    bool running = Call("isClientRunning", value) && (value == "true" || value == "1");
    Log("gfc_isClientRunning -> %s", running ? "true" : "false");
    return running;
}

void __stdcall noscat_shutdown()
{
    Log("gfc_shutdown");
    std::lock_guard<std::mutex> guard(g_lock);
    ClosePipe();
}

bool __stdcall noscat_queryAuthorizationCode(char* b, size_t s) { return QueryPipe("queryAuthorizationCode", b, s); }
bool __stdcall noscat_queryGameAccountName(char* b, size_t s) { return QueryPipe("queryGameAccountName", b, s); }

bool __stdcall noscat_queryGameAccountId(char* b, size_t s)
{
    std::string name;
    return Call("queryGameAccountName", name) && QueryLocal("queryGameAccountId", name, b, s);
}

bool __stdcall noscat_queryGameAccountNumericId(char* b, size_t s) { return QueryLocal("queryGameAccountNumericId", Env("NOSCAT_ACCOUNT_NUMERIC_ID", "1"), b, s); }
bool __stdcall noscat_queryGameLocale(char* b, size_t s) { return QueryLocal("queryGameLocale", Locale(), b, s); }
bool __stdcall noscat_queryClientLocale(char* b, size_t s) { return QueryLocal("queryClientLocale", Locale(), b, s); }
bool __stdcall noscat_queryGameDisplayLocale(char* b, size_t s) { return QueryLocal("queryGameDisplayLocale", Locale(), b, s); }
bool __stdcall noscat_queryGameRegion(char* b, size_t s) { return QueryLocal("queryGameRegion", Env("NOSCAT_GAME_REGION", "EU"), b, s); }
bool __stdcall noscat_queryCurrentBranch(char* b, size_t s) { return QueryLocal("queryCurrentBranch", Env("NOSCAT_BRANCH", "master"), b, s); }
bool __stdcall noscat_sessionId(char* b, size_t s) { return QueryLocal("sessionId", Env("_TNT_SESSION_ID", ""), b, s); }

// Error API: we never raise errors through it, so these only have to be safe to call.
void __stdcall noscat_setErrorCallback(void*, void*) { Log("gfc_setErrorCallback"); }
void __stdcall noscat_setGlobalErrorMessageCallback(void*, void*) { Log("gfc_setGlobalErrorMessageCallback"); }
void* __stdcall noscat_lastError() { return nullptr; }
int __stdcall noscat_lastErrorCode() { return 0; }
void __stdcall noscat_resetError() {}
void* __stdcall noscat_copyError(void*) { return nullptr; }
void __stdcall noscat_freeError(void*) {}
unsigned __stdcall noscat_proxyVersion() { return 0; }

}  // extern "C"

BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) Log("loaded into PID %lu", GetCurrentProcessId());
    return TRUE;
}
