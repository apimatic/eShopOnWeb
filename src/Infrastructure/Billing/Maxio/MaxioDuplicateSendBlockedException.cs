using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Raised by <see cref="MaxioCallScopeHandler"/> when it refuses to re-send a write.
/// </summary>
/// <remarks>
/// Deliberately not an <see cref="System.Net.Http.HttpRequestException"/>: that is the very type the SDK's
/// retry pipeline retries, so refusing with one would make the refusal itself retryable.
/// </remarks>
internal sealed class MaxioDuplicateSendBlockedException : Exception
{
    public MaxioDuplicateSendBlockedException(string method, string? path)
        : base($"Refused to re-send {method} {path}: the request has already been dispatched once.")
    {
    }
}
