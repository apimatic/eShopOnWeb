using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Thrown when a POST that already left this process is about to be resent by the SDK retry pipeline.
/// Must not derive from <see cref="System.Net.Http.HttpRequestException"/> — that type is itself retried.
/// </summary>
internal sealed class DuplicateWritePreventedException : Exception
{
    public DuplicateWritePreventedException()
        : base("A retried write was blocked because the original request may already have reached the billing provider.")
    {
    }
}
