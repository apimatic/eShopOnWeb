namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentOperationException : ApiException
{
    public PaymentOperationException(string message, int statusCode, string? debugId = null, string? issue = null)
        : base(message, statusCode)
    {
        DebugId = debugId;
        Issue = issue;
    }

    public string? DebugId { get; }
    public string? Issue { get; }
}
