using System;
using System.Collections.Generic;
using UnityEngine;
using ZXing;
using ZXing.Common;

namespace Kiosk.QR
{
    /// <summary>
    /// QR-only adapter around ZXing.Net. This class does not call Unity APIs and can be
    /// used on a worker thread after the caller has copied pixels out of WebCamTexture.
    /// </summary>
    public sealed class ZXingQRCodeDecoder : IQRCodeDecoder
    {
        private readonly BarcodeReaderGeneric reader;
        private readonly object syncRoot = new object();
        private byte[] rgbaBuffer;

        public ZXingQRCodeDecoder()
        {
            reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    TryInverted = true,
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
                }
            };
        }

        public bool TryDecode(Color32[] pixels, int width, int height, out string text)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException(nameof(pixels));
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Frame dimensions must be positive.");
            }

            int pixelCount = checked(width * height);
            if (pixels.Length < pixelCount)
            {
                throw new ArgumentException("The pixel buffer is smaller than the supplied frame dimensions.", nameof(pixels));
            }

            lock (syncRoot)
            {
                int byteCount = checked(pixelCount * 4);
                if (rgbaBuffer == null || rgbaBuffer.Length != byteCount)
                {
                    rgbaBuffer = new byte[byteCount];
                }

                for (int pixelIndex = 0, byteIndex = 0; pixelIndex < pixelCount; pixelIndex++, byteIndex += 4)
                {
                    Color32 pixel = pixels[pixelIndex];
                    rgbaBuffer[byteIndex] = pixel.r;
                    rgbaBuffer[byteIndex + 1] = pixel.g;
                    rgbaBuffer[byteIndex + 2] = pixel.b;
                    rgbaBuffer[byteIndex + 3] = pixel.a;
                }

                Result result = reader.Decode(rgbaBuffer, width, height, RGBLuminanceSource.BitmapFormat.RGBA32);
                text = result?.Text;
                return !string.IsNullOrEmpty(text);
            }
        }
    }
}
