namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CommerceException : Exception
{
    public CommerceException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }

    public static CommerceException BadRequest(string code, string message) => new(400, code, message);
    public static CommerceException NotFound(string message) => new(404, "NOT_FOUND", message);
    public static CommerceException Conflict(string code, string message) => new(409, code, message);
}
