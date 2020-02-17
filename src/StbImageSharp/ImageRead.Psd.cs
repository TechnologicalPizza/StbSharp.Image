﻿namespace StbSharp
{
    public static partial class ImageRead
    {
        public static unsafe class Psd
        {
            public struct PsdInfo
            {
                public int channelCount;
                public int compression;
            }

            public static bool Test(ReadContext s)
            {
                var info = new PsdInfo();
                var ri = new ReadState();

                bool success = ParseHeader(s, ref info, ref ri, ScanMode.Type);
                s.Rewind();
                return success;
            }

            public static bool Info(ReadContext s, out ReadState ri)
            {
                var info = new PsdInfo();
                ri = new ReadState();

                bool success = ParseHeader(s, ref info, ref ri, ScanMode.Header);
                s.Rewind();
                return success;
            }

            public static bool DecodeRLE(ReadContext s, byte* p, int pixelCount)
            {
                int count = 0;
                int nleft;
                while ((nleft = (int)(pixelCount - count)) > (0))
                {
                    int len = (int)(s.ReadByte());
                    if ((len) == (128))
                    {
                    }
                    else if ((len) < (128))
                    {
                        len++;
                        if ((len) > (nleft))
                            return false;

                        count += (int)(len);
                        while ((len) != 0)
                        {
                            *p = (byte)(s.ReadByte());
                            p += 4;
                            len--;
                        }
                    }
                    else if ((len) > (128))
                    {
                        len = (int)(257 - len);
                        if ((len) > (nleft))
                            return false;

                        int val = (byte)(s.ReadByte());
                        count += (int)(len);
                        while ((len) != 0)
                        {
                            *p = (byte)(val);
                            p += 4;
                            len--;
                        }
                    }
                }

                return true;
            }

            public static IMemoryResult Load(ReadContext s, ref ReadState ri)
            {
                var info = new PsdInfo();
                if (!ParseHeader(s, ref info, ref ri, ScanMode.Load))
                    return null;

                if (AreValidMad3Sizes(4, ri.Width, ri.Height, 0) == 0)
                {
                    Error("too large");
                    return null;
                }

                byte* _out_ = (byte*)(MAllocMad3(4 * ri.OutDepth / 8, ri.Width, ri.Height, 0));
                if (_out_ == null)
                {
                    Error("outofmem");
                    return null;
                }

                int pixelCount = (int)(ri.Width * ri.Height);
                if (info.compression != 0)
                {
                    s.Skip((int)(ri.Height * info.channelCount * 2));

                    for (int channel = 0; (channel) < (4); channel++)
                    {
                        byte* p;
                        p = _out_ + channel;
                        if ((channel) >= (info.channelCount))
                        {
                            for (int i = 0; (i) < (pixelCount); i++, p += 4)
                                *p = (byte)((channel) == (3) ? 255 : 0);
                        }
                        else
                        {
                            if (!DecodeRLE(s, p, (int)(pixelCount)))
                            {
                                CRuntime.Free(_out_);
                                Error("corrupt");
                                return null;
                            }
                        }
                    }
                }
                else
                {
                    for (int channel = 0; channel < 4; channel++)
                    {
                        if ((channel) >= (info.channelCount))
                        {
                            if (ri.Depth == 16 && ri.RequestedDepth == ri.Depth)
                            {
                                ushort* q = ((ushort*)(_out_)) + channel;
                                ushort val = (ushort)((channel) == (3) ? 65535 : 0);
                                for (int i = 0; (i) < (pixelCount); i++, q += 4)
                                    *q = (ushort)(val);
                            }
                            else
                            {
                                byte* p = _out_ + channel;
                                byte val = (byte)((channel) == (3) ? 255 : 0);
                                for (int i = 0; (i) < (pixelCount); i++, p += 4)
                                    *p = (byte)(val);
                            }
                        }
                        else
                        {
                            if ((ri.OutDepth) == (16))
                            {
                                ushort* q = ((ushort*)(_out_)) + channel;
                                for (int i = 0; (i) < (pixelCount); i++, q += 4)
                                    *q = ((ushort)(s.ReadInt16BE()));
                            }
                            else
                            {
                                byte* p = _out_ + channel;
                                if ((ri.OutDepth) == (16))
                                {
                                    for (int i = 0; (i) < (pixelCount); i++, p += 4)
                                        *p = ((byte)(s.ReadInt16BE() >> 8));
                                }
                                else
                                {
                                    for (int i = 0; (i) < (pixelCount); i++, p += 4)
                                        *p = (byte)(s.ReadByte());
                                }
                            }
                        }
                    }
                }

                if ((info.channelCount) >= (4))
                {
                    if ((ri.OutDepth) == (16))
                    {
                        for (int i = 0; (i) < pixelCount; ++i)
                        {
                            ushort* pixel = (ushort*)(_out_) + 4 * i;
                            if ((pixel[3] != 0) && (pixel[3] != 65535))
                            {
                                float a = (float)(pixel[3] / 65535.0f);
                                float ra = (float)(1f / a);
                                float inv_a = (float)(65535.0f * (1 - ra));
                                pixel[0] = ((ushort)(pixel[0] * ra + inv_a));
                                pixel[1] = ((ushort)(pixel[1] * ra + inv_a));
                                pixel[2] = ((ushort)(pixel[2] * ra + inv_a));
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; (i) < pixelCount; ++i)
                        {
                            byte* pixel = _out_ + 4 * i;
                            if ((pixel[3] != 0) && (pixel[3] != 255))
                            {
                                float a = (float)(pixel[3] / 255f);
                                float ra = (float)(1f / a);
                                float inv_a = (float)(255f * (1 - ra));
                                pixel[0] = ((byte)(pixel[0] * ra + inv_a));
                                pixel[1] = ((byte)(pixel[1] * ra + inv_a));
                                pixel[2] = ((byte)(pixel[2] * ra + inv_a));
                            }
                        }
                    }
                }

                IMemoryResult result = new HGlobalMemoryResult(_out_, ri.Width * ri.Height * ri.OutComponents);
                result = ConvertFormat(result, ref ri);

                return result;
            }

            public static bool ParseHeader(
               ReadContext s, ref PsdInfo info, ref ReadState ri, ScanMode scan)
            {
                if (s.ReadInt32BE() != 0x38425053) // "8BPS"
                    return false;

                if (s.ReadInt16BE() != 1)
                    return false;

                if (scan == ScanMode.Type)
                    return true;

                s.Skip((int)(6));

                info.channelCount = (int)(s.ReadInt16BE());
                if (((info.channelCount) < (0)) || ((info.channelCount) > (16)))
                {
                    Error("wrong channel count");
                    return false;
                }

                ri.Height = (int)(s.ReadInt32BE());
                ri.Width = (int)(s.ReadInt32BE());
                ri.Depth = (int)(s.ReadInt16BE());
                if (ri.Depth != 8 && ri.Depth != 16)
                {
                    Error("unsupported bit depth");
                    return false;
                }
                if (s.ReadInt16BE() != 3)
                {
                    Error("wrong color format");
                    return false;
                }

                s.Skip((int)(s.ReadInt32BE()));
                s.Skip((int)(s.ReadInt32BE()));
                s.Skip((int)(s.ReadInt32BE()));

                info.compression = (int)(s.ReadInt16BE());
                if ((info.compression) > (1))
                {
                    Error("bad compression");
                    return false;
                }

                ri.OutDepth = ri.RequestedDepth ?? ri.Depth;

                if (info.compression == 0)
                    ri.OutDepth = ri.RequestedDepth ?? ri.Depth;
                else
                    ri.OutDepth = 8;

                return true;
            }
        }
    }
}