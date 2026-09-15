using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Sentinel thrown when a write is refused a second send. Must not derive from
/// HttpRequestException — that type is retried by the SDK pipeline.
/// </summary>
internal sealed class DuplicateWritePreventedException : Exception
{
    public DuplicateWritePreventedException()
        : base("A non-idempotent Maxio write was blocked from being sent a second time.")
    {
    }
}
