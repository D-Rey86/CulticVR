/******************************************************************************
 * The MIT License (MIT)
 *
 * Copyright (c) 2015-2026 Baldur Karlsson
 * Copyright (c) 2014 Crytek
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 ******************************************************************************/


#include "third_party/md5/md5.h"
using byte=unsigned char;
constexpr uint32_t FOURCC_DXBC=0x43425844;
struct FileHeader {uint32_t fourcc;uint32_t hashValue[4];uint32_t containerVersion,fileLength,numChunks;};
static bool HashContainer(void *ByteCode, size_t BytecodeLength)
{
  if(BytecodeLength < sizeof(FileHeader) || ByteCode == NULL)
    return false;

  FileHeader *header = (FileHeader *)ByteCode;

  if(header->fourcc != FOURCC_DXBC)
    return false;

  if(header->fileLength != (uint32_t)BytecodeLength)
    return false;

  MD5_CTX md5ctx = {};
  MD5_Init(&md5ctx);

  // the hashable data starts immediately after the hash.
  byte *data = (byte *)&header->containerVersion;
  uint32_t length = uint32_t(BytecodeLength - offsetof(FileHeader, containerVersion));

  // we need to know the number of bits for putting in the trailing padding.
  uint32_t numBits = length * 8;
  uint32_t numBitsPart2 = (numBits >> 2) | 1;

  // MD5 works on 64-byte chunks, process the first set of whole chunks, leaving 0-63 bytes left
  // over
  uint32_t leftoverLength = length % 64;
  MD5_Update(&md5ctx, data, length - leftoverLength);

  data += length - leftoverLength;

  uint32_t block[16] = {};
  static_assert(sizeof(block) == 64, "Block is not properly sized for MD5 round");

  // normally MD5 finishes by appending a 1 bit to the bitstring. Since we are only appending bytes
  // this would be an 0x80 byte (the first bit is considered to be the MSB). Then it pads out with
  // zeroes until it has 56 bytes in the last block and appends appends the message length as a
  // 64-bit integer as the final part of that block.
  // in other words, normally whatever is leftover from the actual message gets one byte appended,
  // then if there's at least 8 bytes left we'll append the length. Otherwise we pad that block with
  // 0s and create a new block with the length at the end.
  // Or as the original RFC/spec says: padding is always performed regardless of whether the
  // original buffer already ended in exactly a 56 byte block.
  //
  // The DXBC finalisation is slightly different (previous work suggests this is due to a bug in the
  // original implementation and it was maybe intended to be exactly MD5?):
  //
  // The length provided in the padding block is not 64-bit properly: the second dword with the high
  // bits is instead the number of nybbles(?) with 1 OR'd on. The length is also split, so if it's
  // in
  // a padding block the low bits are in the first dword and the upper bits in the last. If there's
  // no padding block the low dword is passed in first before the leftovers of the message and then
  // the upper bits at the end.

  // if the leftovers uses at least 56, we can't fit both the trailing 1 and the 64-bit length, so
  // we need a padding block and then our own block for the length.
  if(leftoverLength >= 56)
  {
    // pass in the leftover data padded out to 64 bytes with zeroes
    MD5_Update(&md5ctx, data, leftoverLength);

    block[0] = 0x80;    // first padding bit is 1
    MD5_Update(&md5ctx, block, 64 - leftoverLength);

    // the final block contains the number of bits in the first dword, and the weird upper bits
    block[0] = numBits;
    block[15] = numBitsPart2;

    // process this block directly, we're replacing the call to MD5_Final here manually
    MD5_Update(&md5ctx, block, 64);
  }
  else
  {
    // the leftovers mean we can put the padding inside the final block. But first we pass the "low"
    // number of bits:
    MD5_Update(&md5ctx, &numBits, sizeof(numBits));

    if(leftoverLength)
      MD5_Update(&md5ctx, data, leftoverLength);

    uint32_t paddingBytes = 64 - leftoverLength - 4;

    // prepare the remainder of this block, starting with the 0x80 padding start right after the
    // leftovers and the first part of the bit length above.
    block[0] = 0x80;
    // then add the remainder of the 'length' here in the final part of the block
    memcpy(((byte *)block) + paddingBytes - 4, &numBitsPart2, 4);

    MD5_Update(&md5ctx, block, paddingBytes);
  }

  header->hashValue[0] = md5ctx.a;
  header->hashValue[1] = md5ctx.b;
  header->hashValue[2] = md5ctx.c;
  header->hashValue[3] = md5ctx.d;

  return true;
}
