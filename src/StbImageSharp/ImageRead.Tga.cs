﻿using System;

namespace StbSharp
{
    public static partial class ImageRead
    {
        public static unsafe class Tga
        {
            public struct TgaInfo
            {
                public int offset;
                public int colormap_type;
                public int image_type;
                public bool is_RLE;
                public int palette_start;
                public int palette_len;
                public int palette_bits;
                public int x_origin;
                public int y_origin;
                public int bits_per_pixel;
                public int inverted;
            }

            public static int GetComponentCount(int bits_per_pixel, bool is_grey, out int bitsPerComp)
            {
                bitsPerComp = 8;

                switch (bits_per_pixel)
                {
                    case 8:
                        return 1;

                    case 15:
                    case 16:
                        if (((bits_per_pixel) == (16)) && is_grey)
                            return 2;

                        bitsPerComp = 16;
                        return 3;

                    case 24:
                    case 32:
                        return (int)(bits_per_pixel / 8);

                    default:
                        return 0;
                }
            }

            public static bool ParseHeader(ReadContext s, ref TgaInfo info, ref ReadState ri, ScanMode scan)
            {
                info.offset = s.ReadByte();

                info.colormap_type = (int)(s.ReadByte());
                if ((info.colormap_type) > (1))
                    return false;

                info.image_type = (int)(s.ReadByte());
                if (info.image_type >= 8)
                {
                    info.image_type -= 8;
                    info.is_RLE = true;
                }

                if (info.colormap_type == 1)
                    if ((info.image_type != 1) && (info.image_type != 9))
                        return false;

                info.palette_start = (int)(s.ReadInt16LE());
                info.palette_len = (int)(s.ReadInt16LE());

                info.palette_bits = (int)(s.ReadByte());
                if (info.palette_bits != 8 &&
                    info.palette_bits != 15 &&
                    info.palette_bits != 16 &&
                    info.palette_bits != 24 &&
                    info.palette_bits != 32)
                    return false;

                info.x_origin = (int)(s.ReadInt16LE());
                info.y_origin = (int)(s.ReadInt16LE());

                if (info.image_type != 2 &&
                    info.image_type != 3 &&
                    info.image_type != 10 &&
                    info.image_type != 11)
                    return false;

                s.Skip(9);

                if (scan == ScanMode.Type)
                    return true;

                ri.Width = (int)(s.ReadInt16LE());
                if ((ri.Width) < (1))
                    return false;

                ri.Height = (int)(s.ReadInt16LE());
                if ((ri.Height) < (1))
                    return false;

                info.bits_per_pixel = (int)(s.ReadByte());

                info.inverted = s.ReadByte();
                info.inverted = 1 - ((info.inverted >> 5) & 1);

                // use the number of bits from the palette if paletted
                if (info.palette_bits != 0)
                {
                    if (info.bits_per_pixel != 8 &&
                        info.bits_per_pixel != 16)
                        return false;

                    ri.Components = (int)(GetComponentCount(info.palette_bits, false, out ri.Depth));
                }
                else
                {
                    ri.Components = GetComponentCount(info.bits_per_pixel, info.image_type == 3, out ri.Depth);
                }

                if (ri.Components == 0)
                {
                    Error("bad format");
                    return false;
                }

                ri.OutComponents = ri.Components;
                ri.OutDepth = ri.Depth;
                return true;
            }

            public static bool Info(ReadContext s, out ReadState ri)
            {
                var info = new TgaInfo();
                ri = new ReadState();

                bool success = ParseHeader(s, ref info, ref ri, ScanMode.Header);
                s.Rewind();
                return success;
            }

            public static bool Test(ReadContext s)
            {
                var info = new TgaInfo();
                var ri = new ReadState();

                bool success = ParseHeader(s, ref info, ref ri, ScanMode.Type);
                s.Rewind();
                return success;
            }

            public static void ReadRgb16(ReadContext s, byte* _out_)
            {
                ushort px = (ushort)(s.ReadInt16LE());
                ushort fiveBitMask = (ushort)(31);
                int r = (int)((px >> 10) & fiveBitMask);
                int g = (int)((px >> 5) & fiveBitMask);
                int b = (int)(px & fiveBitMask);
                _out_[0] = ((byte)((r * 255) / 31));
                _out_[1] = ((byte)((g * 255) / 31));
                _out_[2] = ((byte)((b * 255) / 31));
            }

            public static IMemoryResult Load(ReadContext s, ref ReadState ri)
            {
                var info = new TgaInfo();
                if (!ParseHeader(s, ref info, ref ri, ScanMode.Load))
                    return null;

                if (AreValidMad3Sizes((int)(ri.Width), (int)(ri.Height), (int)(ri.OutComponents), 0) == 0)
                {
                    Error("too large");
                    return null;
                }

                byte* _out_ = (byte*)(MAllocMad3((int)(ri.Width), (int)(ri.Height), (int)(ri.OutComponents), 0));
                if (_out_ == null)
                {
                    Error("outofmem");
                    return null;
                }

                byte* raw_data = stackalloc byte[4];
                raw_data[0] = 0;

                s.Skip((int)(info.offset));

                int i;
                int j;
                if (info.colormap_type == 0 && !info.is_RLE && ri.OutDepth == 8)
                {
                    for (i = 0; (i) < (ri.Height); ++i)
                    {
                        int row = (int)((info.inverted) != 0 ? ri.Height - i - 1 : i);
                        byte* tga_row = _out_ + row * ri.Width * ri.OutComponents;
                        s.ReadBytes(new Span<byte>(tga_row, ri.Width * ri.OutComponents));
                    }
                }
                else
                {
                    byte* tga_palette = null;
                    if ((info.colormap_type) != 0)
                    {
                        s.Skip((info.palette_start));
                        tga_palette = (byte*)(MAllocMad2((info.palette_len), (ri.OutComponents), 0));
                        if (tga_palette == null)
                        {
                            CRuntime.Free(_out_);
                            Error("outofmem");
                            return null;
                        }

                        if (ri.Depth == 16)
                        {
                            byte* pal_entry = tga_palette;
                            for (i = 0; (i) < (info.palette_len); ++i)
                            {
                                ReadRgb16(s, pal_entry);
                                pal_entry += ri.OutComponents;
                            }
                        }
                        else if (!s.ReadBytes(new Span<byte>(tga_palette, info.palette_len * ri.OutComponents)))
                        {
                            CRuntime.Free(_out_);
                            CRuntime.Free(tga_palette);
                            Error("bad palette");
                            return null;
                        }
                    }

                    int RLE_count = 0;
                    int RLE_repeating = 0;
                    int read_next_pixel = 1;

                    for (i = 0; (i) < (ri.Width * ri.Height); ++i)
                    {
                        if (info.is_RLE)
                        {
                            if ((RLE_count) == 0)
                            {
                                int RLE_cmd = (int)(s.ReadByte());
                                RLE_count = (int)(1 + (RLE_cmd & 127));
                                RLE_repeating = (int)(RLE_cmd >> 7);
                                read_next_pixel = 1;
                            }
                            else if (RLE_repeating == 0)
                                read_next_pixel = 1;
                        }
                        else
                            read_next_pixel = 1;

                        if ((read_next_pixel) != 0)
                        {
                            if ((info.colormap_type) != 0)
                            {
                                int pal_idx = (int)(((info.bits_per_pixel) == (8)) ? s.ReadByte() : s.ReadInt16LE());
                                if ((pal_idx) >= (info.palette_len))
                                    pal_idx = 0;

                                pal_idx *= (int)(ri.OutComponents);
                                for (j = 0; (j) < (ri.OutComponents); ++j)
                                    raw_data[j] = (byte)(tga_palette[pal_idx + j]);
                            }
                            else if (ri.OutDepth == 16)
                                ReadRgb16(s, raw_data);
                            else
                                for (j = 0; (j) < (ri.OutComponents); ++j)
                                    raw_data[j] = (byte)(s.ReadByte());

                            read_next_pixel = 0;
                        }

                        for (j = 0; (j) < (ri.OutComponents); ++j)
                            _out_[i * ri.OutComponents + j] = (byte)(raw_data[j]);

                        RLE_count--;
                    }

                    if ((info.inverted) != 0)
                    {
                        for (j = 0; (j * 2) < (ri.Height); ++j)
                        {
                            int index1 = (int)(j * ri.Width * ri.OutComponents);
                            int index2 = (int)((ri.Height - 1 - j) * ri.Width * ri.OutComponents);
                            for (i = (int)(ri.Width * ri.OutComponents); (i) > (0); --i)
                            {
                                byte tmp = (byte)(_out_[index1]);
                                _out_[index1] = (byte)(_out_[index2]);
                                _out_[index2] = (byte)(tmp);
                                ++index1;
                                ++index2;
                            }
                        }
                    }

                    if (tga_palette != null)
                        CRuntime.Free(tga_palette);
                }

                if (((ri.OutComponents) >= (3)) && ri.OutDepth == 8)
                {
                    byte* tga_pixel = _out_;
                    for (i = 0; (i) < (ri.Width * ri.Height); ++i)
                    {
                        byte tmp = (byte)(tga_pixel[0]);
                        tga_pixel[0] = (byte)(tga_pixel[2]);
                        tga_pixel[2] = (byte)(tmp);
                        tga_pixel += ri.OutComponents;
                    }
                }

                IMemoryResult result = new HGlobalMemoryResult(_out_, ri.OutComponents * ri.Width * ri.Height);
                result = ConvertFormat(result, ref ri);
                return result;
            }
        }
    }
}