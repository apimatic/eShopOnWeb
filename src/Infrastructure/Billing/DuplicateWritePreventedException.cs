using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Thrown when a second outbound write is blocked. Not an <see cref="System.Net.Http.HttpRequestException"/>,
/// so the SDK retry pipeline does not resend it.
/// </summary>
public sealed class DuplicateWritePreventedException : Exception
{
    public DuplicateWritePreventedException()
        : base("A duplicate billing write was blocked before it reached the provider.")
    {
    }
}
