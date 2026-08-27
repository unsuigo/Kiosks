using System;
using System.Collections.Generic;

namespace Kiosk.QR
{
    /// <summary>
    /// Suppresses the same decoded value for a configurable monotonic-time interval.
    /// </summary>
    public sealed class QRCodeDuplicateFilter
    {
        private readonly Dictionary<string, double> lastSeenAt =
            new Dictionary<string, double>(StringComparer.Ordinal);

        public QRCodeDuplicateFilter(double cooldownSeconds)
        {
            CooldownSeconds = Math.Max(0d, cooldownSeconds);
        }

        public double CooldownSeconds { get; set; }

        public bool ShouldEmit(string value, double nowSeconds)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            double cooldown = Math.Max(0d, CooldownSeconds);
            if (lastSeenAt.TryGetValue(value, out double lastSeen) && nowSeconds - lastSeen < cooldown)
            {
                return false;
            }

            lastSeenAt[value] = nowSeconds;
            RemoveExpiredEntries(nowSeconds, cooldown);
            return true;
        }

        public void Reset()
        {
            lastSeenAt.Clear();
        }

        private void RemoveExpiredEntries(double nowSeconds, double cooldown)
        {
            if (lastSeenAt.Count < 64)
            {
                return;
            }

            double retention = Math.Max(60d, cooldown * 2d);
            var expired = new List<string>();
            foreach (KeyValuePair<string, double> pair in lastSeenAt)
            {
                if (nowSeconds - pair.Value > retention)
                {
                    expired.Add(pair.Key);
                }
            }

            for (int i = 0; i < expired.Count; i++)
            {
                lastSeenAt.Remove(expired[i]);
            }
        }
    }
}
