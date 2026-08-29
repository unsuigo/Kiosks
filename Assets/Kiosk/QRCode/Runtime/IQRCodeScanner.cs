using System;

namespace Kiosk.QR
{
    public interface IQRCodeScanner
    {
        event Action<string> QrCodeDetected;
        event Action<QRCodeScannerState, string> StatusChanged;

        bool IsScanning { get; }
        QRCodeScannerState State { get; }
        string LastError { get; }

        void StartScanning();
        void StopScanning();
    }
}