using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal responded that a payer action (e.g. 3-D Secure challenge) is required in a
/// browser. This integration is drivable without a browser, so a challenge is reported
/// rather than worked around.
/// </summary>
public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message) : base(message) { }
}