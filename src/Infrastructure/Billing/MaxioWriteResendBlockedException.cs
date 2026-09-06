using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Raised when the resilience pipeline tried to resend a write that had already gone out once.
/// </summary>
/// <remarks>
/// This deliberately does not derive from <see cref="System.Net.Http.HttpRequestException"/>: that
/// is the very type the SDK retry pipeline treats as retryable, so a refusal expressed as one would
/// itself be retried. Its meaning is "the outcome of the single send we allowed is unknown", not
/// "the write failed" - the caller settles it by re-reading provider state.
/// </remarks>
internal sealed class MaxioWriteResendBlockedException : Exception
{
    public MaxioWriteResendBlockedException(string message) : base(message)
    {
    }
}
