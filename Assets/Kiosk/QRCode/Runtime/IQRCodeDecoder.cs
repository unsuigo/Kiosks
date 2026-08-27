using UnityEngine;

namespace Kiosk.QR
{
    public interface IQRCodeDecoder
    {
        bool TryDecode(Color32[] pixels, int width, int height, out string text);
    }
}
