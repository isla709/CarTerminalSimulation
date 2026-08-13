#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>

typedef int (__cdecl *probe_fn)(const wchar_t*,double*,int*);
typedef int (__cdecl *thumbnail_fn)(const wchar_t*,const wchar_t*);

int wmain(int argc,wchar_t** argv){
    if(argc<3)return 2;
    HMODULE module=LoadLibraryW(L"TerminalFfmpeg.Native.dll");
    if(!module){fwprintf(stderr,L"LoadLibrary failed: %lu\n",GetLastError());return 3;}
    probe_fn probe=(probe_fn)GetProcAddress(module,"tf_probe");
    thumbnail_fn thumbnail=(thumbnail_fn)GetProcAddress(module,"tf_thumbnail");
    if(!probe||!thumbnail)return 4;
    double fps=0;int audio=0;int rc=probe(argv[1],&fps,&audio);
    if(rc<0){fprintf(stderr,"probe=%d\n",rc);return 5;}
    printf("fps=%.3f audio=%d\n",fps,audio);
    rc=thumbnail(argv[1],argv[2]);
    if(rc<0){fprintf(stderr,"thumbnail=%d\n",rc);return 6;}
    FreeLibrary(module);return 0;
}
