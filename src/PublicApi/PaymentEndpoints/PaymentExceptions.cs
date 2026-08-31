using System;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CommerceException : Exception
{
    public CommerceException(int statusCode, string title, string message) : base(message)
    {
        StatusCode = statusCode;
        Title = title;
    }

    public int StatusCode { get; }
    public string Title { get; }
}

public sealed class PayPalException : Exception
{
    public PayPalException(int statusCode, string name, string message, string? issue, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        Issue = issue;
        DebugId = debugId;
    }

    public int StatusCode { get; }
    public string Name { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
}
