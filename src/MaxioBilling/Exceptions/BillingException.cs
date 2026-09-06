namespace Microsoft.eShopWeb.MaxioBilling.Exceptions;

/// <summary>
/// The one exception type the billing integration surfaces. Every SDK exception, transport
/// failure and unreadable payload is translated into this at the integration boundary.
/// <para>
/// The message is always caller-safe: SDK and framework exception text is kept in
/// <see cref="Exception.InnerException"/> for the logs and never placed on the wire.
/// </para>
/// </summary>
public class BillingException : Exception
{
    public BillingException(BillingFailureKind kind, string message, Exception? innerException = null, int? providerStatusCode = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }

    /// <summary>What went wrong, in terms the caller's boundary can map to a status code.</summary>
    public BillingFailureKind Kind { get; }

    /// <summary>The HTTP status Maxio returned, when there was one.</summary>
    public int? ProviderStatusCode { get; }
}
