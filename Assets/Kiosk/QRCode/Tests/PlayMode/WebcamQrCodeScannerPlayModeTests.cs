using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Kiosk.QR.Tests
{
    public sealed class WebcamQrCodeScannerPlayModeTests
    {
        [UnityTest]
        public IEnumerator StopScanning_CalledRepeatedly_IsSafeAndIdempotent()
        {
            var gameObject = new GameObject("QR Scanner Test");
            var scanner = gameObject.AddComponent<WebcamQrCodeScanner>();
            yield return null;

            Assert.DoesNotThrow(scanner.StopScanning);
            Assert.DoesNotThrow(scanner.StopScanning);
            Assert.That(scanner.IsScanning, Is.False);
            Assert.That(scanner.State, Is.EqualTo(QRCodeScannerState.Stopped));

            Object.Destroy(gameObject);
            yield return null;
        }
    }
}
