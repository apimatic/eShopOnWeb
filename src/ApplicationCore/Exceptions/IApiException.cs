namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An exception that carries an HTTP status and a caller-actionable message, so the API surface can
/// translate a failed flow into a meaningful response instead of an opaque 500.
/// </summary>
public interface IApiException
{
    int StatusCode { get; }
    string? DebugId { get; }
}
