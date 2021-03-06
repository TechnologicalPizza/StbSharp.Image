﻿using System;
using System.Buffers.Binary;

namespace StbSharp.ImageRead
{
    public static unsafe class Psd
    {
        public const int HeaderSize = 6;

        public struct PsdInfo
        {
            public int channelCount;
            public int compression;
        }

        public static bool Test(ReadOnlySpan<byte> header)
        {
            if (header.Length < HeaderSize)
                return false;

            // TODO: check byte per byte instead?

            if (BinaryPrimitives.ReadInt32BigEndian(header) != 0x38425053) // "8BPS"
                return false;

            if (BinaryPrimitives.ReadInt16BigEndian(header.Slice(sizeof(int))) != 1)
                return false;

            return true;
        }

        public static bool Info(ImageBinReader s, out ReadState ri)
        {
            ri = new ReadState();
            var info = new PsdInfo();
            return ParseHeader(s, ri, ref info);
        }

        public static bool DecodeRLE(ImageBinReader s, byte* destination, int pixelCount)
        {
            int count = 0;
            int nleft;
            while ((nleft = pixelCount - count) > 0)
            {
                int len = s.ReadByte();
                if (len == 128)
                {
                }
                else if (len < 128)
                {
                    len++;
                    if (len > nleft)
                        return false;

                    count += len;
                    while (len != 0)
                    {
                        *destination = s.ReadByte();
                        destination += 4;
                        len--;
                    }
                }
                else if (len > 128)
                {
                    len = 257 - len;
                    if (len > nleft)
                        return false;

                    int val = s.ReadByte();
                    count += len;
                    while (len != 0)
                    {
                        *destination = (byte)val;
                        destination += 4;
                        len--;
                    }
                }
            }

            return true;
        }

        public static IMemoryHolder Load(BinReader s, ReadState ri)
        {
            var info = new PsdInfo();
            if (!ParseHeader(s, ri, ref info))
                return null;

            if (AreValidMad3Sizes(4, ri.Width, ri.Height, 0) == 0)
            {
                s.Error(ErrorCode.TooLarge);
                return null;
            }

            byte* _out_ = (byte*)MAllocMad3((4 * ri.OutDepth + 7) / 8, ri.Width, ri.Height, 0);
            if (_out_ == null)
            {
                s.Error(ErrorCode.OutOfMemory);
                return null;
            }

            int pixelCount = ri.Width * ri.Height;
            if (info.compression != 0)
            {
                s.Skip(ri.Height * info.channelCount * 2);

                for (int channel = 0; channel < 4; channel++)
                {
                    byte* dst;
                    dst = _out_ + channel;
                    if (channel >= info.channelCount)
                    {
                        for (int i = 0; i < pixelCount; i++, dst += 4)
                            *dst = (byte)(channel == 3 ? 255 : 0);
                    }
                    else
                    {
                        if (!DecodeRLE(s, dst, pixelCount))
                        {
                            CRuntime.Free(_out_);
                            throw new StbImageReadException(ErrorCode.Corrupt);
                        }
                    }
                }
            }
            else
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    if (channel >= info.channelCount)
                    {
                        if (ri.Depth == 16)
                        {
                            ushort* q = ((ushort*)_out_) + channel;
                            ushort val = (ushort)(channel == 3 ? 65535 : 0);
                            for (int i = 0; i < pixelCount; i++, q += 4)
                                *q = val;
                        }
                        else
                        {
                            byte* p = _out_ + channel;
                            byte val = (byte)(channel == 3 ? 255 : 0);
                            for (int i = 0; i < pixelCount; i++, p += 4)
                                *p = val;
                        }
                    }
                    else
                    {
                        if (ri.OutDepth == 16)
                        {
                            ushort* q = ((ushort*)_out_) + channel;
                            for (int i = 0; i < pixelCount; i++, q += 4)
                                *q = (ushort)s.ReadInt16BE();
                        }
                        else
                        {
                            byte* p = _out_ + channel;
                            if (ri.OutDepth == 16)
                            {
                                for (int i = 0; i < pixelCount; i++, p += 4)
                                    *p = (byte)(s.ReadInt16BE() >> 8);
                            }
                            else
                            {
                                for (int i = 0; i < pixelCount; i++, p += 4)
                                    *p = s.ReadByte();
                            }
                        }
                    }
                }
            }

            if (info.channelCount >= 4)
            {
                if (ri.OutDepth == 16)
                {
                    for (int i = 0; i < pixelCount; ++i)
                    {
                        ushort* pixel = (ushort*)_out_ + 4 * i;
                        if ((pixel[3] != 0) && (pixel[3] != 65535))
                        {
                            float a = pixel[3] / 65535.0f;
                            float ra = 1f / a;
                            float inv_a = 65535.0f * (1 - ra);
                            pixel[0] = (ushort)(pixel[0] * ra + inv_a);
                            pixel[1] = (ushort)(pixel[1] * ra + inv_a);
                            pixel[2] = (ushort)(pixel[2] * ra + inv_a);
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < pixelCount; ++i)
                    {
                        byte* pixel = _out_ + 4 * i;
                        if ((pixel[3] != 0) && (pixel[3] != 255))
                        {
                            float a = pixel[3] / 255f;
                            float ra = 1f / a;
                            float inv_a = 255f * (1 - ra);
                            pixel[0] = (byte)(pixel[0] * ra + inv_a);
                            pixel[1] = (byte)(pixel[1] * ra + inv_a);
                            pixel[2] = (byte)(pixel[2] * ra + inv_a);
                        }
                    }
                }
            }

            IMemoryHolder result = new HGlobalMemoryHolder(
                _out_, (ri.Width * ri.Height * ri.OutComponents * ri.OutDepth + 7) / 8);

            var errorCode = ConvertFormat(result, ri, out var convertedResult);
            if (errorCode != ErrorCode.Ok)
                return null;
            return convertedResult;
        }

        public static bool ParseHeader(ImageBinReader s, ReadState ri, ref PsdInfo info)
        {
            Span<byte> tmp = stackalloc byte[HeaderSize];
            if (!s.TryReadBytes(tmp))
                return false;

            if (!Test(tmp))
                throw new StbImageReadException(ErrorCode.UnknownFormat);

            // TODO: figure out what this skips
            s.Skip(6);

            info.channelCount = s.ReadInt16BE();
            if ((info.channelCount < 0) || (info.channelCount > 16))
                throw new StbImageReadException(ErrorCode.BadChannelCount);

            ri.Height = s.ReadInt32BE();
            ri.Width = s.ReadInt32BE();
            ri.Depth = s.ReadInt16BE();
            if (ri.Depth != 8 && ri.Depth != 16)
                throw new StbImageReadException(ErrorCode.UnsupportedBitDepth);

            if (s.ReadInt16BE() != 3)
                throw new StbImageReadException(ErrorCode.BadColorType);

            s.Skip(s.ReadInt32BE());
            s.Skip(s.ReadInt32BE());
            s.Skip(s.ReadInt32BE());

            info.compression = s.ReadInt16BE();
            if (info.compression > 1)
                throw new StbImageReadException(ErrorCode.BadCompression);

            ri.OutDepth = ri.Depth;

            if (info.compression == 0)
                ri.OutDepth = ri.Depth;
            else
                ri.OutDepth = 8;

            return true;
        }
    }
}