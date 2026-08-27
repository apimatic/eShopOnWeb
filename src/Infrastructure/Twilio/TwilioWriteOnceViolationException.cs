using System;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal sealed class TwilioWriteOnceViolationException : Exception
{
    public TwilioWriteOnceViolationException()
        : base("A duplicate Twilio write was blocked.")
    {
    }
}
