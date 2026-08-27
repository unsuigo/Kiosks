using System;
using System.Collections.Generic;

namespace Kiosk.QR
{
    public enum QRCodeScannerState
    {
        Stopped,
        Initializing,
        Scanning,
        Error
    }

    public interface IQRCodeScanner
    {
        event Action<string> QrCodeDetected;
        event Action<QRCodeScannerState, string> StatusChanged;
        bool IsScanning { get; }
        QRCodeScannerState State { get; }
        string LastError { get; }
        string ActiveCameraName { get; }
        IReadOnlyList<string> AvailableCameraNames { get; }
        void StartScanning();
        void StopScanning();
    }
}
