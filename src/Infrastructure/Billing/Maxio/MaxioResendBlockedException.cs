using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Raised when a write was about to be put on the wire a second time and the write-once guard
/// refused it.
/// <para>
/// It deliberately derives from <see cref="Exception"/> and <em>not</em> from
/// <see cref="System.Net.Http.HttpRequestException"/>: that is the type the SDK's retry pipeline
/// resends on, so refusing with it would make the refusal itself retryable.
/// </para>
/// </summary>
internal sealed class MaxioResendBlockedException : Exception
{
    public MaxioResendBlockedException(string message) : base(message)
    {
    }
}
