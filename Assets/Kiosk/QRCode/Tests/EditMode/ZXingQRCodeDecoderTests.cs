using NUnit.Framework;
using UnityEngine;
using ZXing;
using ZXing.QrCode;
using ZXing.Rendering;

namespace Kiosk.QR.Tests
{
    public sealed class ZXingQRCodeDecoderTests
    {
        [Test]
        public void TryDecode_GeneratedKnownQr_ReturnsExactText()
        {
            const string expected = "TICKET-ABC-123";
            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Width = 256,
                    Height = 256,
                    Margin = 4,
                    CharacterSet = "UTF-8"
                }
            };

            PixelData image = writer.Write(expected);
            Color32[] pixels = ToUnityPixels(image);
            var decoder = new ZXingQRCodeDecoder();

            bool decoded = decoder.TryDecode(pixels, image.Width, image.Height, out string actual);

            Assert.That(decoded, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void TryDecode_BlankImage_ReturnsFalse()
        {
            const int width = 128;
            const int height = 128;
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.white;
            }

            var decoder = new ZXingQRCodeDecoder();
            bool decoded = decoder.TryDecode(pixels, width, height, out string actual);

            Assert.That(decoded, Is.False);
            Assert.That(actual, Is.Null.Or.Empty);
        }

        private static Color32[] ToUnityPixels(PixelData image)
        {
            var pixels = new Color32[image.Width * image.Height];
            for (int i = 0, source = 0; i < pixels.Length; i++, source += 4)
            {
                byte blue = image.Pixels[source];
                byte green = image.Pixels[source + 1];
                byte red = image.Pixels[source + 2];
                byte alpha = image.Pixels[source + 3];
                pixels[i] = new Color32(red, green, blue, alpha);
            }

            return pixels;
        }
    }
}
