using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentChallengeRequiredException : Exception
{
    public PaymentChallengeRequiredException()
        : base("PayPal required a shopper challenge that needs a browser. This API does not collect a payer approval round-trip.")
    {
    }
}
