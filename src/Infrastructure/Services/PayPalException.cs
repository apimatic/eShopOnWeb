using System;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class PayPalException : Exception
{
    public string PayPalErrorName { get; }
    public string? DebugId { get; }

    public PayPalException(string paypalErrorName, string message, string? debugId = null)
        : base(message)
    {
        PayPalErrorName = paypalErrorName;
        DebugId = debugId;
    }
}
