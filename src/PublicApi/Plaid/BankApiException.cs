using System;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>
/// The app can't carry out the request. Mapped to the given status and reason by
/// <see cref="Middleware.ExceptionMiddleware"/>.
/// </summary>
public class BankApiException : Exception
{
    public int StatusCode { get; }

    public BankApiException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static BankApiException BadRequest(string message) => new(400, message);
    public static BankApiException NotFound(string message) => new(404, message);
    public static BankApiException Conflict(string message) => new(409, message);
    public static BankApiException Unprocessable(string message) => new(422, message);
}
