using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Failure of a Maxio Advanced Billing interaction. The message is always
/// caller-safe: it is safe to surface on the API boundary. The provider's HTTP
/// status is preserved so the boundary can map failure kinds faithfully.
/// </summary>
public sealed class MaxioBillingException : Exception
{
    public HttpStatusCode Status { get; }

    public MaxioBillingException(HttpStatusCode status, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Status = status;
    }
}
