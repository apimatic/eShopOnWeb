using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Raised by <see cref="MaxioWriteOnceHandler"/> when the SDK's retry pipeline tries to resend a write that
/// <see cref="MaxioWriteGuard"/> has already allowed once.
/// </summary>
/// <remarks>
/// The first send may well have reached Maxio, so this means "the outcome is unknown", not "nothing
/// happened" — the caller settles it by re-reading Maxio state.
/// </remarks>
public sealed class MaxioDuplicateSendBlockedException : Exception
{
    public MaxioDuplicateSendBlockedException()
        : base("A retry of a non-idempotent Maxio write was blocked; the first send may already have taken effect.")
    {
    }
}
