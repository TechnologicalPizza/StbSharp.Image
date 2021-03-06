using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace StbSharp.ImageRead
{
    public delegate void ReadProgressCallback(double progress, Rect? rectangle);

    public delegate void StateReadyDelegate(ReadState state);

    public delegate void OutputPixelLineDelegate(
        ReadState state, AddressingMajor addressing, int line, int start,
        int spacing, ReadOnlySpan<byte> pixels);

    public delegate void OutputPixelDelegate(
        ReadState state, int x, int y, ReadOnlySpan<byte> pixel);

    public class ReadState
    {
        public event ReadProgressCallback? Progress;

        public StateReadyDelegate? StateReadyCallback;
        public OutputPixelLineDelegate? OutputPixelLineCallback;
        public OutputPixelDelegate? OutputPixelCallback;
        public CancellationToken CancellationToken;

        public int Width;
        public int Height;
        public int Depth;
        public int Components;

        public bool Progressive;
        public ImageOrientation Orientation;

        public int OutDepth;
        public int OutComponents;

        public bool UnpremultiplyOnLoad { get; set; } = true;
        public bool DeIphoneFlag { get; set; } = true;

        public ReadState()
        {
        }

        public void StateReady()
        {
            StateReadyCallback?.Invoke(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OutputPixelLine(
            AddressingMajor addressing, int line, int start, int spacing, ReadOnlySpan<byte> pixels)
        {
            OutputPixelLineCallback?.Invoke(this, addressing, line, start, spacing, pixels);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OutputPixelLine(
            AddressingMajor addressing, int line, int start, ReadOnlySpan<byte> pixels)
        {
            OutputPixelLine(addressing, line, start, 1, pixels);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OutputPixel(int x, int y, ReadOnlySpan<byte> pixels)
        {
            OutputPixelCallback?.Invoke(this, x, y, pixels);
        }
    }
}