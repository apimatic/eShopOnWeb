using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalProviderException : Exception
{
    public bool IsOperatorActionable { get; }

    public PayPalProviderException(string message, bool isOperatorActionable = false)
        : base(message)
    {
        IsOperatorActionable = isOperatorActionable;
    }

    public PayPalProviderException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
