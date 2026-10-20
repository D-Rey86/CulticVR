// Runtime transformations of exact supported shader fingerprints. No game bytecode is stored here.
#pragma once
#include <Windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <cstring>
#include <vector>
#include <algorithm>
#include "DxbcChecksum.h"

namespace ShaderPatch {
using Words = std::vector<uint32_t>;
using Bytes = std::vector<unsigned char>;
enum Kind { Unknown, Point, Palette };
inline BCRYPT_ALG_HANDLE hashProvider = nullptr;
inline bool Initialize() { return hashProvider || BCryptOpenAlgorithmProvider(&hashProvider,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0; }
inline uint32_t Read(const void *p) { uint32_t v; memcpy(&v,p,4); return v; }
inline void Append(Bytes &out,uint32_t v) { auto p=reinterpret_cast<unsigned char*>(&v);out.insert(out.end(),p,p+4); }
inline Kind Identify(const void *bytes,size_t length) {
    if(!bytes || !hashProvider || (length!=3932 && length!=692))return Unknown;
    unsigned char digest[32]{};
    if(BCryptHash(hashProvider,nullptr,0,static_cast<PUCHAR>(const_cast<void*>(bytes)),static_cast<ULONG>(length),digest,32)<0)return Unknown;
    const unsigned char point[]={0x94,0x2a,0x12,0x8f,0x54,0x57,0x89,0xfb,0xa0,0x45,0xdb,0xe6,0xea,0xac,0x2b,0x07,0xb3,0x35,0x26,0x06,0xde,0xed,0xe5,0x25,0x27,0x1e,0xbe,0x8b,0x4f,0x72,0x23,0x3e};
    const unsigned char palette[]={0x30,0xe1,0xb2,0x03,0xb9,0x97,0xb6,0x80,0x35,0xea,0xc0,0x7d,0xcc,0x66,0x42,0x65,0xcf,0x5d,0x72,0xd0,0x90,0xfc,0x7a,0x69,0x49,0x26,0x7f,0x7b,0xc0,0xf1,0x9c,0xae};
    if(!memcmp(digest,point,32))return Point;
    if(!memcmp(digest,palette,32))return Palette;
    return Unknown;
}
struct Chunk { size_t offset,size;uint32_t tag; };
inline bool Parse(const void *input,size_t length,std::vector<Chunk>&chunks,Words&words,size_t &codeIndex) {
    if(!input || length<32 || length>65536)return false;
    auto b=static_cast<const unsigned char*>(input);
    uint32_t count=Read(b+28);
    if(Read(b)!=FOURCC_DXBC || Read(b+20)!=1 || Read(b+24)!=length || count>16 || count==0 || 32+4*count>length)return false;
    bool found=false;size_t previous=32+4*count;
    for(uint32_t i=0;i<count;++i) {
        size_t off=Read(b+32+4*i);
        if(off<previous || off>length-8)return false;
        size_t size=Read(b+off+4);
        if(size>length-off-8)return false;
        auto tag=Read(b+off);chunks.push_back({off,size+8,tag});previous=off+size+8;
        if(tag==0x52444853 || tag==0x58454853) {
            if(found || size<8 || size%4)return false;
            found=true;codeIndex=i;words.resize(size/4);memcpy(words.data(),b+off+8,size);
        }
    }
    if(!found || words[0]!=0x40 || words[1]!=words.size())return false;
    for(size_t i=2;i<words.size();) {
        auto n=(words[i]>>24)&127;
        if(n==0 || n>words.size()-i)return false;
        i+=n;
    }
    return previous==length;
}
inline bool Transform(Words &words,Kind kind,int mode) {
    Words result(words.begin(),words.begin()+2);
    unsigned temps=0,anchor=0,loop=0,sqrt=0,close=0;
    const Words pointAnchor={0x7000038,0x100082,0,0x10003a,0,0x10000a,3};
    const Words paletteEnd={0x9000037,0x100082,0,0x10002a,2,0x10001a,2,0x10003a,0};
    for(size_t i=2;i<words.size();) {
        size_t n=(words[i]>>24)&127;
        Words instruction(words.begin()+i,words.begin()+i+n);
        uint32_t op=words[i]&0x7ff;
        if(op==104) {
            if(n!=2 || instruction[1]!=(kind==Point?6u:3u))return false;
            ++temps; ++instruction[1];
        }
        if(kind==Palette && op==48) { ++loop;result.insert(result.end(),{0x5000036,0x100042,3,0x4001,0x7f800000}); }
        if(kind==Palette && op==75) {
            ++sqrt;
            result.insert(result.end(),{0x5000036,0x100012,3,0x10001a,2,
                0x7000038,0x100022,3,0x10002a,3,0x4001,0x3f800080,
                0x7000031,0x100022,3,0x10001a,2,0x10001a,3,
                0x304001f,0x10001a,3});
        }
        result.insert(result.end(),instruction.begin(),instruction.end());
        if(kind==Point && instruction==pointAnchor) {
            ++anchor;
            result.insert(result.end(),{0x7000018,0x100012,6,0x10003a,0,0x4001,0,0x304001f,0x10000a,6});
            if(mode==1)result.insert(result.end(),{0x8000036,0x1020f2,0,0x4002,0,0,0,0x3f800000,0x100003e});
            else if(mode==2)result.insert(result.end(),{0x304000d,0x4001,0xffffffff});
            else return false;
            result.push_back(0x1000015);
        }
        if(kind==Palette && instruction==paletteEnd) {
            ++close;result.insert(result.end(),{0x9000037,0x100042,3,0x10002a,2,0x10000a,3,0x10002a,3,0x1000015});
        }
        i+=n;
    }
    if(temps!=1 || (kind==Point && (anchor!=1 || words.size()!=893)) ||
       (kind==Palette && (loop!=1 || sqrt!=1 || close!=1 || words.size()!=125)))return false;
    result[1]=static_cast<uint32_t>(result.size());words.swap(result);return true;
}
inline bool Make(const void *input,size_t length,Kind kind,int mode,Bytes &out) {
    if(kind==Unknown || Identify(input,length)!=kind)return false;
    std::vector<Chunk> chunks;Words words;size_t codeIndex=0;
    if(!Parse(input,length,chunks,words,codeIndex) || !Transform(words,kind,mode))return false;
    auto b=static_cast<const unsigned char*>(input);
    out.assign(b,b+32+4*chunks.size());
    memset(out.data()+4,0,16);
    for(size_t i=0;i<chunks.size();++i) {
        uint32_t offset=static_cast<uint32_t>(out.size());memcpy(out.data()+32+4*i,&offset,4);
        if(i==codeIndex) {
            Append(out,chunks[i].tag);Append(out,static_cast<uint32_t>(words.size()*4));
            auto data=reinterpret_cast<const unsigned char*>(words.data());out.insert(out.end(),data,data+words.size()*4);
        } else out.insert(out.end(),b+chunks[i].offset,b+chunks[i].offset+chunks[i].size);
    }
    uint32_t size=static_cast<uint32_t>(out.size());memcpy(out.data()+24,&size,4);
    return HashContainer(out.data(),out.size());
}
}
