namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Marks an exception that already knows the HTTP status the API should return to its caller, so the
/// API's error boundary can map it deliberately (a caller-actionable 4xx stays a 4xx, an outage is a
/// 5xx) instead of collapsing everything to 500.
/// </summary>
public interface IApiStatusCodeException
{
    int StatusCode { get; }
    string? Issue { get; }
}
