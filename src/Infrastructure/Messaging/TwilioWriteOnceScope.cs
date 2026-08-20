using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class TwilioWriteOnceScope
{
    private static readonly AsyncLocal<Slot?> Current = new();

    public static IDisposable Begin()
    {
        var slot = new Slot();
        Current.Value = slot;
        return new Popper();
    }

    public static bool TryMarkSent()
    {
        var slot = Current.Value;
        if (slot is null)
        {
            return true;
        }

        if (slot.Sent)
        {
            return false;
        }

        slot.Sent = true;
        return true;
    }

    private sealed class Slot
    {
        public bool Sent;
    }

    private sealed class Popper : IDisposable
    {
        public void Dispose() => Current.Value = null;
    }
}
