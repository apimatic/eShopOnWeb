using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The card issuer requires the shopper to complete a challenge (3DS/SCA) in a browser
/// before the payment can proceed. This integration only supports direct, non-interactive
/// card processing, so this failure aborts the flow instead of building an approval round-trip.
/// </summary>
public class PaymentActionRequiredException : Exception
{
    public PaymentActionRequiredException()
        : base("The card issuer requires the shopper to approve this payment interactively (3DS/SCA challenge). Direct card processing cannot complete this payment.")
    {
    }
}
