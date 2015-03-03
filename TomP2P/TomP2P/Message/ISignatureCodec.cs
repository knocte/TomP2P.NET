﻿using TomP2P.Extensions.Netty;
using TomP2P.Extensions.Netty.Buffer;

namespace TomP2P.Message
{
    public interface ISignatureCodec
    {
        byte[] Encode();

        ISignatureCodec Decode(byte[] encodedData);

        ISignatureCodec Write(ByteBuf buffer);

        ISignatureCodec Read(ByteBuf buffer);

        int SignatureSize { get; set; }
    }
}