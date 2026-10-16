#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#include <windows.h>
#include <shellapi.h>
#include <filesystem>
#include <fstream>
#include <array>
#include <algorithm>
#include <string>
#include <vector>
#include <stdexcept>
#include <cstdint>
namespace fs = std::filesystem;
static uint16_t u16(const unsigned char* p) { return p[0] | p[1] << 8; }
static uint32_t u32(const unsigned char* p) { return p[0] | p[1] << 8 | p[2] << 16 | uint32_t(p[3]) << 24; }
static std::wstring wide(const std::string& s) {
    int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s.data(), (int)s.size(), nullptr, 0);
    if (!n) throw std::runtime_error("Invalid filename");
    std::wstring r(n, 0); MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s.data(), (int)s.size(), r.data(), n); return r;
}
static std::wstring quote(const std::wstring& s) {
    std::wstring r=L"\""; unsigned n=0;
    for (auto c:s) { if(c==L'\\'){++n;continue;} r.append(n*(c==L'"'?2:1),L'\\'); n=0; if(c==L'"')r+=L'\\';r+=c; }
    r.append(n*2,L'\\'); return r+L'"';
}
static std::vector<unsigned char> read(std::ifstream& s, uint64_t at, size_t length) {
    std::vector<unsigned char> b(length); s.seekg(at); s.read((char*)b.data(), length);
    if ((size_t)s.gcount()!=length) throw std::runtime_error("Truncated setup"); return b;
}
struct InflateStream {
    unsigned char* nextIn; unsigned char* nextOut;
    char* message; void* state;
    uint32_t availIn, availOut;
};
static_assert(sizeof(InflateStream)==40, "Unexpected x64 .NET compression stream layout");
using InflateInit = int (__cdecl*)(InflateStream*, int);
using Inflate = int (__cdecl*)(InflateStream*, int);
using InflateEnd = int (__cdecl*)(InflateStream*);
static void inflateEntry(std::ifstream& input, uint64_t dataAt, uint32_t compressedSize, uint32_t expectedSize,
                         uint32_t expectedCrc, std::ofstream& output, uint32_t* crcTable, HMODULE library) {
    auto init=(InflateInit)GetProcAddress(library,"CompressionNative_InflateInit2_");
    auto inflate=(Inflate)GetProcAddress(library,"CompressionNative_Inflate");
    auto finish=(InflateEnd)GetProcAddress(library,"CompressionNative_InflateEnd");
    if(!init||!inflate||!finish) throw std::runtime_error("Compression runtime is incomplete");
    InflateStream stream{};
    if(init(&stream,-15)!=0) throw std::runtime_error("Cannot initialize the bundled ZIP decompressor");
    std::array<unsigned char,131072> inputBuffer{}, outputBuffer{};
    uint32_t loaded=0, producedTotal=0, crc=0xffffffff; bool ended=false;
    try {
        while(!ended) {
            if(stream.availIn==0 && loaded<compressedSize) {
                auto chunk=read(input,dataAt+loaded,std::min<uint32_t>((uint32_t)inputBuffer.size(),compressedSize-loaded));
                std::copy(chunk.begin(),chunk.end(),inputBuffer.begin());
                stream.nextIn=inputBuffer.data(); stream.availIn=(uint32_t)chunk.size(); loaded+=(uint32_t)chunk.size();
            }
            stream.nextOut=outputBuffer.data(); stream.availOut=(uint32_t)outputBuffer.size();
            int result=inflate(&stream,0); size_t count=outputBuffer.size()-stream.availOut;
            if(count) {
                if(producedTotal+count>expectedSize) throw std::runtime_error("Inflated engine entry exceeds declared size");
                for(size_t i=0;i<count;++i) crc=(crc>>8)^crcTable[(crc^outputBuffer[i])&255];
                output.write((char*)outputBuffer.data(),count); if(!output) throw std::runtime_error("Cannot write inflated engine entry");
                producedTotal+=(uint32_t)count;
            }
            if(result==1) { ended=true; break; }
            if(result!=0) throw std::runtime_error("Corrupt compressed engine entry");
            if(stream.availIn==0 && loaded==compressedSize && count==0) throw std::runtime_error("Truncated compressed engine entry");
        }
    if(stream.availIn!=0 || loaded!=compressedSize || producedTotal!=expectedSize || (crc^0xffffffff)!=expectedCrc)
            throw std::runtime_error("Compressed engine entry length mismatch");
    } catch(...) { finish(&stream); throw; }
    finish(&stream);
    output.flush(); if(!output) throw std::runtime_error("Cannot finish inflated engine entry");
}
int WINAPI wWinMain(HINSTANCE,HINSTANCE,PWSTR,int) {
    fs::path work; bool owned=false, cooperative=false; HMODULE compressionLibrary=nullptr;
    try {
        int modeCount=0; auto modeArgs=CommandLineToArgvW(GetCommandLineW(),&modeCount);
        if(!modeArgs)throw std::runtime_error("Invalid command line");
        for(int i=1;i<modeCount;++i)if(std::wstring(modeArgs[i])==L"--update-stage")cooperative=true;
        LocalFree(modeArgs);
        wchar_t name[32768],temp[32768]; GetModuleFileNameW(nullptr,name,32768); GetTempPathW(32768,temp);
        fs::path package(name); work=fs::path(temp)/L"CarTerminalSimulation"/L"Setup"/(std::to_wstring(GetCurrentProcessId())+L"-"+std::to_wstring(GetTickCount64()));
        if(!fs::create_directories(work)) throw std::runtime_error("Temporary directory collision");
        owned=true;
        std::ifstream input(package,std::ios::binary); input.seekg(0,std::ios::end); uint64_t size=input.tellg();
        if(size<22 || size>2ull*1024*1024*1024)throw std::runtime_error("Invalid setup size");
        auto tail=read(input,size-std::min<uint64_t>(size,65557),std::min<uint64_t>(size,65557));
        int end=-1; for(int i=(int)tail.size()-22;i>=0;--i)if(u32(&tail[i])==0x06054b50 && i+22+u16(&tail[i+20])==(int)tail.size()){end=i;break;}
        if(end<0)throw std::runtime_error("Setup archive missing");
        uint32_t at=u32(&tail[end+16]); int count=u16(&tail[end+10]);
        if(count==65535 || u16(&tail[end+4]) || u16(&tail[end+6]))throw std::runtime_error("Unsupported archive");
        auto directoryAt=at;
        std::string slot;
        for(int i=0;i<count;++i) {
            auto h=read(input,at,46); if(u32(h.data())!=0x02014b50)throw std::runtime_error("Invalid directory");
            auto n=read(input,at+46,u16(&h[28])); std::string path((char*)n.data(),n.size());
            at+=46+u16(&h[28])+u16(&h[30])+u16(&h[32]);
            if(path!="setup-package.json")continue;
            if(!slot.empty() || u16(&h[10])!=0 || u32(&h[24])>4*1024*1024)throw std::runtime_error("Invalid setup metadata");
            auto local=read(input,u32(&h[42]),30);
            auto bytes=read(input,uint64_t(u32(&h[42]))+30+u16(&local[26])+u16(&local[28]),u32(&h[24]));
            std::string json((char*)bytes.data(),bytes.size()); auto key=json.find("\"engine\"");
            if(key==std::string::npos)throw std::runtime_error("Engine descriptor missing");
            auto colon=json.find(':',key+8),start=json.find('"',colon+1),end=json.find('"',start+1);
            if(colon==std::string::npos||start==std::string::npos||end==std::string::npos)throw std::runtime_error("Invalid engine descriptor");
            slot=json.substr(start+1,end-start-1);
            if(slot.rfind("engines/",0)!=0 || slot.find("..")!=std::string::npos || slot.find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789/.-_")!=std::string::npos)throw std::runtime_error("Unsafe engine descriptor");
        }
        if(slot.empty())throw std::runtime_error("Setup metadata missing");
        at=directoryAt;
        uint32_t table[256];for(uint32_t i=0;i<256;++i){auto c=i;for(int j=0;j<8;++j)c=(c>>1)^(0xedb88320u&(0u-(c&1)));table[i]=c;}
        const std::string prefix=".Updater/"+slot+"/";
        const std::string runtimePrefix=".Updater/runtimes/net8-x64/";
        fs::path compressionPath; bool foundInflater=false;
        for(int i=0;i<count;++i) {
            auto h=read(input,at,46); if(u32(h.data())!=0x02014b50)throw std::runtime_error("Invalid directory");
            auto n=read(input,at+46,u16(&h[28])); std::string path((char*)n.data(),n.size());
            at+=46+u16(&h[28])+u16(&h[30])+u16(&h[32]);
            const auto separator=path.find_last_of('/');
            if(path.rfind(runtimePrefix,0)!=0 || separator==std::string::npos || path.substr(separator+1)!="System.IO.Compression.Native.dll")continue;
            if(foundInflater || u16(&h[10])!=0 || u32(&h[20])!=u32(&h[24]) || u32(&h[24])>16*1024*1024)
                throw std::runtime_error("Invalid bundled compression runtime");
            auto local=read(input,u32(&h[42]),30);
            if(u32(local.data())!=0x04034b50 || u16(&local[8])!=0)throw std::runtime_error("Invalid compression runtime entry");
            auto dataAt=uint64_t(u32(&h[42]))+30+u16(&local[26])+u16(&local[28]);
            auto bytes=read(input,dataAt,u32(&h[24])); uint32_t crc=0xffffffff;
            for(auto c:bytes)crc=(crc>>8)^table[(crc^c)&255];
            if((crc^0xffffffff)!=u32(&h[16]))throw std::runtime_error("Compression runtime integrity check failed");
            auto relative=fs::path(wide(path)); compressionPath=work/relative; fs::create_directories(compressionPath.parent_path());
            std::ofstream output(compressionPath,std::ios::binary); output.write((char*)bytes.data(),bytes.size()); output.close();
            if(!output)throw std::runtime_error("Cannot extract compression runtime");
            foundInflater=true;
        }
        if(!foundInflater)throw std::runtime_error("Setup package is missing its bundled compression runtime");
        compressionLibrary=LoadLibraryExW(compressionPath.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if(!compressionLibrary)throw std::runtime_error("Cannot load the bundled compression runtime");
        fs::path engine; size_t total=0; int extracted=0;
        at=directoryAt;
        for(int i=0;i<count;++i) {
            auto h=read(input,at,46); if(u32(h.data())!=0x02014b50)throw std::runtime_error("Invalid directory");
            auto n=read(input,at+46,u16(&h[28])); std::string path((char*)n.data(),n.size());
            at+=46+u16(&h[28])+u16(&h[30])+u16(&h[32]);
            if(path.empty())throw std::runtime_error("Empty setup entry");
            const bool engineEntry=path.rfind(prefix,0)==0;
            const bool runtimeEntry=path.rfind(runtimePrefix,0)==0;
            if((!engineEntry && !runtimeEntry) || path.back()=='/')continue;
            if(path.find('\\')!=std::string::npos || path.find(':')!=std::string::npos)throw std::runtime_error("Unsafe path");
            fs::path relative(wide(path)); for(auto& part:relative)if(part==L".."||part==L".")throw std::runtime_error("Unsafe path");
            uint16_t method=u16(&h[10]); uint32_t compressed=u32(&h[20]), length=u32(&h[24]);
            if(relative.is_absolute() || (u16(&h[8])&1) || (method!=0 && method!=8) || (method==0 && compressed!=length))throw std::runtime_error("Unsupported engine entry");
            total+=length; if(total>512ull*1024*1024 || ++extracted>10000)throw std::runtime_error("Engine too large");
            auto local=read(input,u32(&h[42]),30); if(u32(local.data())!=0x04034b50)throw std::runtime_error("Invalid local entry");
            if(u16(&local[8])!=method || (u16(&local[6])&1))throw std::runtime_error("ZIP entry metadata mismatch");
            auto dataAt=uint64_t(u32(&h[42]))+30+u16(&local[26])+u16(&local[28]);
            auto destination=work/relative;
            if(destination!=compressionPath) {
                fs::create_directories(destination.parent_path()); std::ofstream output(destination,std::ios::binary);
                if(method==0) {
                    uint32_t crc=0xffffffff;
                    for(uint32_t done=0;done<length;) {
                        auto data=read(input,dataAt+done,std::min<uint32_t>(131072,length-done));
                        for(auto c:data)crc=(crc>>8)^table[(crc^c)&255];
                        output.write((char*)data.data(),data.size()); if(!output)throw std::runtime_error("Cannot extract engine"); done+=(uint32_t)data.size();
                    }
                    if((crc^0xffffffff)!=u32(&h[16]))throw std::runtime_error("Engine integrity check failed");
                } else inflateEntry(input,dataAt,compressed,length,u32(&h[16]),output,table,compressionLibrary);
                output.close(); if(!output)throw std::runtime_error("Cannot finish extracting engine");
            }
            if(engineEntry && path.size()>11 && path.substr(path.size()-11)=="/update.exe") {
                if(!engine.empty())throw std::runtime_error("Multiple setup engines"); engine=destination;
            }
        }
        if(engine.empty())throw std::runtime_error("Setup engine missing");
        const auto privateRuntime=work/L".Updater"/L"runtimes"/L"net8-x64";
        if(!fs::exists(privateRuntime/L"host"/L"fxr"))throw std::runtime_error("Private updater runtime is incomplete");
        SetEnvironmentVariableW(L"DOTNET_ROOT",privateRuntime.c_str());
        SetEnvironmentVariableW(L"DOTNET_ROOT_X64",privateRuntime.c_str());
        SetEnvironmentVariableW(L"DOTNET_MULTILEVEL_LOOKUP",L"0");
        FreeLibrary(compressionLibrary); compressionLibrary=nullptr;
        int argc=0; auto argv=CommandLineToArgvW(GetCommandLineW(),&argc);
        std::wstring command=quote(engine.wstring())+L" --setup --package "+quote(package.wstring());
        for(int i=1;i<argc;++i)command+=L" "+quote(argv[i]); LocalFree(argv);
        STARTUPINFOW si{};si.cb=sizeof(si);PROCESS_INFORMATION pi{};
        if(!CreateProcessW(engine.c_str(),command.data(),nullptr,nullptr,FALSE,0,nullptr,engine.parent_path().c_str(),&si,&pi))throw std::runtime_error("Cannot start setup engine");
        CloseHandle(pi.hThread);WaitForSingleObject(pi.hProcess,INFINITE);DWORD code=1;GetExitCodeProcess(pi.hProcess,&code);CloseHandle(pi.hProcess);
        {std::error_code ec;fs::remove_all(work,ec);} return (int)code;
    } catch(...) {
        if(compressionLibrary)FreeLibrary(compressionLibrary);
        if(owned){std::error_code ec;fs::remove_all(work,ec);}
        if(!cooperative)MessageBoxW(nullptr,L"安装程序无法启动或文件已损坏。请重新下载完整安装包。\n现有程序和用户数据未被修改。",L"车载定位终端模拟系统安装",MB_OK|MB_ICONERROR);return 1;
    }
}
