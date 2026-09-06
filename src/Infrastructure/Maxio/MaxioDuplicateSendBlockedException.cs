using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the send guard refuses a resend inside a <see cref="MaxioSingleSendScope"/>.
/// </summary>
/// <remarks>
/// Deliberately not an <c>HttpRequestException</c>: that is the type the SDK's retry pipeline treats as a
/// transport failure, so refusing with one would make the refusal itself retryable. This type propagates
/// out unwrapped and is translated at the integration boundary.
/// </remarks>
public sealed class MaxioDuplicateSendBlockedException : Exception
{
    public MaxioDuplicateSendBlockedException()
        : base("A retry of a non-idempotent billing request was blocked; the original request may already have been received.")
    {
    }
}
