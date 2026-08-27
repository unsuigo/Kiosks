using NUnit.Framework;

namespace Kiosk.QR.Tests
{
    public sealed class QRCodeDuplicateFilterTests
    {
        [Test]
        public void ShouldEmit_SuppressesSameValueUntilCooldownExpires()
        {
            var filter = new QRCodeDuplicateFilter(2d);

            Assert.That(filter.ShouldEmit("TICKET-ABC-123", 10d), Is.True);
            Assert.That(filter.ShouldEmit("TICKET-ABC-123", 11.999d), Is.False);
            Assert.That(filter.ShouldEmit("TICKET-ABC-123", 12d), Is.True);
        }

        [Test]
        public void ShouldEmit_AllowsDifferentValuesImmediately()
        {
            var filter = new QRCodeDuplicateFilter(10d);

            Assert.That(filter.ShouldEmit("TICKET-A", 1d), Is.True);
            Assert.That(filter.ShouldEmit("TICKET-B", 1.1d), Is.True);
        }
    }
}
