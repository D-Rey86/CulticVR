// Private native receipts are test inputs only; never embedded in the runtime helper.
#include "../../src/CulticVR.RenderingNative/RenderingNative.cpp"
#include <fstream>
#include <iostream>
#include <stdexcept>
static void Need(bool b,const char *m){if(!b)throw std::runtime_error(m);}
static ShaderPatch::Bytes Load(const std::string &path) {
    std::ifstream f(path,std::ios::binary);Need(f.good(),"Missing private shader receipt");
    return {std::istreambuf_iterator<char>(f),std::istreambuf_iterator<char>()};
}
static ID3D11PixelShaderVtbl shaderVtable{};
static ID3D11PixelShader source{&shaderVtable},math{&shaderVtable},discard{&shaderVtable},palette{&shaderVtable},other{&shaderVtable};
static ID3D11PixelShader *bound;
static unsigned calls,retained,released;
static bool reject;
static ULONG STDMETHODCALLTYPE Retain(ID3D11PixelShader*){++retained;return 1;}
static ULONG STDMETHODCALLTYPE Release(ID3D11PixelShader*){++released;return 0;}
static HRESULT STDMETHODCALLTYPE MockCreate(ID3D11Device*,const void*,SIZE_T length,ID3D11ClassLinkage*,ID3D11PixelShader **result){
    ++calls;
    if(!result)return S_FALSE;
    if(reject && length!=3932 && length!=692){*result=nullptr;return E_FAIL;}
    *result=length==4012?&math:length==3988?&discard:length==840?&palette:length==692?&other:&source;return S_OK;
}
static void STDMETHODCALLTYPE MockSet(ID3D11DeviceContext*,ID3D11PixelShader *shader,ID3D11ClassInstance *const*,UINT){bound=shader;}
int main(int argc,char **argv){try {
    Need(argc==2,"Private analysis directory argument required");Need(ShaderPatch::Initialize(),"SHA provider");
    std::string root=argv[1];
    auto point=Load(root+"/original-8292.dxbc"),pal=Load(root+"/original-2719.dxbc");
    Need(ShaderPatch::Identify(point.data(),point.size())==ShaderPatch::Point,"Point allowlist");
    Need(ShaderPatch::Identify(pal.data(),pal.size())==ShaderPatch::Palette,"Palette allowlist");
    for(auto entry:{std::make_pair(1,"zero-attenuation-8292.dxbc"),std::make_pair(2,"discard-zero-attenuation-8292.dxbc")}) {
        ShaderPatch::Bytes result;Need(ShaderPatch::Make(point.data(),point.size(),ShaderPatch::Point,entry.first,result),"Point patch");
        Need(result==Load(root+"/"+entry.second),"Exact measured lighting variant incl signatures/checksum");
    }
    ShaderPatch::Bytes result;Need(ShaderPatch::Make(pal.data(),pal.size(),ShaderPatch::Palette,1,result),"Palette patch");
    Need(result==Load(root+"/palette-tight-2719.dxbc"),"Exact measured palette variant incl signatures/checksum");
    for(size_t size=0;size<point.size();++size)Need(ShaderPatch::Identify(point.data(),size)==ShaderPatch::Unknown,"Truncation not recognized");
    for(size_t offset=0;offset<point.size();++offset) {
        auto mutated=point;mutated[offset]^=1;Need(ShaderPatch::Identify(mutated.data(),mutated.size())==ShaderPatch::Unknown,"Changed byte fails allowlist");
    }
    auto malformed=point;memset(malformed.data()+32,0xff,4);
    std::vector<ShaderPatch::Chunk> chunks;ShaderPatch::Words words;size_t code=0;
    Need(!ShaderPatch::Parse(malformed.data(),malformed.size(),chunks,words,code),"Malformed offset rejected");
    Need(!ShaderPatch::Make(point.data(),point.size(),ShaderPatch::Palette,1,result),"Mismatched kind rejected");
    Need(!ShaderPatch::Make(point.data(),point.size(),ShaderPatch::Point,3,result),"Invalid mode rejected");
    shaderVtable.AddRef=Retain;shaderVtable.Release=Release;
    device=reinterpret_cast<ID3D11Device*>(uintptr_t(1));context=reinterpret_cast<ID3D11DeviceContext*>(uintptr_t(2));
    originalCreate=MockCreate;originalSet=MockSet;ID3D11PixelShader *created=nullptr;
    Create(device,point.data(),point.size(),nullptr,&created);
    Need(created==&source && pairCount==1 && calls==3 && retained==1,"Native shader identity and both cached variants");
    for(int setting:{0,1,2,0}) {
        RenderingSelect(setting,0);Set(context,&source,nullptr,0);
        Need(bound==(setting==0?&source:setting==1?&math:&discard),"Original/math/discard/original live sequence");
    }
    RenderingSelect(2,1);Set(context,&other,nullptr,0);Need(bound==&other,"Unrelated shader unchanged");
    Set(reinterpret_cast<ID3D11DeviceContext*>(uintptr_t(3)),&source,nullptr,0);Need(bound==&source,"Other context unchanged");
    Set(context,&source,nullptr,1);Need(bound==&source,"Class linkage unchanged");
    auto previous=calls;Create(device,malformed.data(),malformed.size(),nullptr,&created);Need(calls==previous+1,"Unknown creation unchanged");
    reject=true;Create(device,point.data(),point.size(),nullptr,&created);
    Need(created==&source && pairCount==1 && failures==1,"Candidate failure preserves original success");reject=false;
    previous=calls;Create(device,pal.data(),pal.size(),nullptr,&created);
    Need(created==&other && pairCount==2 && calls==previous+2 && ready==7,"Palette creation caches one variant and retains original identity");
    for(int setting:{0,1,0}) {RenderingSelect(1,setting);Set(context,&other,nullptr,0);Need(bound==(setting?&palette:&other),"Palette OFF/ON/OFF");}
    RenderingSelect(9,0);Set(context,&source,nullptr,0);Need(bound==&source,"Invalid lighting value restores original");
    previous=calls;Need(Create(device,point.data(),point.size(),nullptr,nullptr)==S_FALSE && calls==previous+1 && pairCount==2,"Native validation-only creation unchanged");
    previous=calls;Create(reinterpret_cast<ID3D11Device*>(uintptr_t(3)),point.data(),point.size(),nullptr,&created);
    Need(calls==previous+1 && pairCount==2,"Other device creation unchanged");
    Create(device,point.data(),point.size(),reinterpret_cast<ID3D11ClassLinkage*>(uintptr_t(4)),&created);
    Need(pairCount==2,"Linked creation unchanged");
    auto releaseBefore=released;pairCount.store(16);Create(device,point.data(),point.size(),nullptr,&created);
    Need(pairCount==16 && failures==2 && released==releaseBefore+2 && created==&source,"Full bounded cache releases replacements and preserves native result");
    std::cout<<"PASS exact prototype bytes/checksums, every-byte allowlist mutation, truncated/malformed input and live binding/fallback controls\n";
    return 0;
}catch(const std::exception &e){std::cerr<<e.what()<<"\n";return 1;}}
