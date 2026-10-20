// Optional D3D11 rendering trials. Unsupported bytecode, linkage and contexts pass through unchanged.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define CINTERFACE
#define D3D11_NO_HELPERS
#include <Windows.h>
#include <d3d11.h>
#include <atomic>
#include "ShaderPatch.h"
#include "third_party/minhook/include/MinHook.h"
using CreatePS=decltype(ID3D11DeviceVtbl::CreatePixelShader);
using SetPS=decltype(ID3D11DeviceContextVtbl::PSSetShader);
static CreatePS originalCreate;
static SetPS originalSet;
static ID3D11Device *device;
static ID3D11DeviceContext *context;
static std::atomic<unsigned> selection{0},pairCount{0},ready{0},failures{0};
static SRWLOCK pairLock=SRWLOCK_INIT;
static bool running;
struct Pair { ID3D11PixelShader *original,*math,*discard;ShaderPatch::Kind kind; };
static Pair pairs[16]{};
static HRESULT STDMETHODCALLTYPE Create(ID3D11Device *self,const void *bytes,SIZE_T length,
    ID3D11ClassLinkage *linkage,ID3D11PixelShader **result) {
    HRESULT hr=originalCreate(self,bytes,length,linkage,result);
    if(self!=device || !result || FAILED(hr) || !*result || linkage)return hr;
    try {
        auto kind=ShaderPatch::Identify(bytes,length);
        if(kind==ShaderPatch::Unknown)return hr;
        ShaderPatch::Bytes transformed;
        ID3D11PixelShader *math=nullptr,*discard=nullptr;
        if(!ShaderPatch::Make(bytes,length,kind,1,transformed) ||
           FAILED(originalCreate(self,transformed.data(),transformed.size(),nullptr,&math)) || !math) {
            failures.fetch_add(1);return hr;
        }
        if(kind==ShaderPatch::Point) {
            if(!ShaderPatch::Make(bytes,length,kind,2,transformed) ||
               FAILED(originalCreate(self,transformed.data(),transformed.size(),nullptr,&discard)) || !discard)failures.fetch_add(1);
        }
        AcquireSRWLockExclusive(&pairLock);
        auto index=pairCount.load(std::memory_order_relaxed);
        if(index<16) {
            (*result)->lpVtbl->AddRef(*result);
            pairs[index]={*result,math,discard,kind};
            pairCount.store(index+1,std::memory_order_release);
            ready.fetch_or(kind==ShaderPatch::Palette?4u:(discard?3u:1u));
        } else {math->lpVtbl->Release(math);if(discard)discard->lpVtbl->Release(discard);failures.fetch_add(1);}
        ReleaseSRWLockExclusive(&pairLock);
    } catch(...) { failures.fetch_add(1); } // Allocation failure must never change native creation's return.
    return hr;
}
static void STDMETHODCALLTYPE Set(ID3D11DeviceContext *self,ID3D11PixelShader *shader,
    ID3D11ClassInstance *const *instances,UINT count) {
    auto chosen=selection.load(std::memory_order_relaxed);
    if(chosen && self==context && shader && !count) {
        auto size=pairCount.load(std::memory_order_acquire);
        for(unsigned i=0;i<size;++i) if(shader==pairs[i].original) {
            auto &p=pairs[i];
            if(p.kind==ShaderPatch::Palette) {if(chosen&4)shader=p.math;}
            else {if((chosen&3)==1)shader=p.math;else if((chosen&3)==2 && p.discard)shader=p.discard;}
            break;
        }
    }
    originalSet(self,shader,instances,count);
}
extern "C" __declspec(dllexport) int __cdecl RenderingStart(void *nativeResource) {
    if(running)return 0;
    if(!nativeResource || !ShaderPatch::Initialize())return 1;
    auto resource=static_cast<ID3D11Resource*>(nativeResource);
    ID3D11Resource *verified=nullptr;
    if(FAILED(resource->lpVtbl->QueryInterface(resource,IID_ID3D11Resource,reinterpret_cast<void**>(&verified))))return 2;
    verified->lpVtbl->GetDevice(verified,&device);verified->lpVtbl->Release(verified);
    if(!device)return 3;
    device->lpVtbl->GetImmediateContext(device,&context);
    if(!context) {device->lpVtbl->Release(device);device=nullptr;return 4;}
    auto createAddress=reinterpret_cast<void*>(device->lpVtbl->CreatePixelShader);
    auto setAddress=reinterpret_cast<void*>(context->lpVtbl->PSSetShader);
    int error=0;auto init=MH_Initialize();
    if(init!=MH_OK && init!=MH_ERROR_ALREADY_INITIALIZED)error=5;
    else if(MH_CreateHook(createAddress,reinterpret_cast<void*>(&Create),reinterpret_cast<void**>(&originalCreate))!=MH_OK)error=6;
    else if(MH_CreateHook(setAddress,reinterpret_cast<void*>(&Set),reinterpret_cast<void**>(&originalSet))!=MH_OK) {MH_RemoveHook(createAddress);error=7;}
    else {
        auto enable=MH_EnableHook(createAddress);
        if(enable!=MH_OK || MH_EnableHook(setAddress)!=MH_OK) {
            MH_DisableHook(createAddress);MH_RemoveHook(createAddress);MH_RemoveHook(setAddress);error=8;
        }
    }
    if(error) {context->lpVtbl->Release(context);device->lpVtbl->Release(device);context=nullptr;device=nullptr;return error;}
    running=true;return 0;
}
extern "C" __declspec(dllexport) void __cdecl RenderingSelect(int lighting,int palette) {
    selection.store((lighting>=0 && lighting<=2?static_cast<unsigned>(lighting):0u)|(palette?4u:0u),std::memory_order_relaxed);
}
extern "C" __declspec(dllexport) unsigned __cdecl RenderingReady() {return ready.load();}
extern "C" __declspec(dllexport) unsigned __cdecl RenderingSelection() {return selection.load();}
extern "C" __declspec(dllexport) unsigned __cdecl RenderingFailures() {return failures.load();}
// Hooks and retained shader/device references live until process exit. Do not FreeLibrary while callbacks exist.
// OFF is a cheap pass-through. No bind counters, per-frame diagnostics, new render targets or shader compilation on switching.
