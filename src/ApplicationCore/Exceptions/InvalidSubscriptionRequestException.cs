using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

// Rejected before any billing-provider call: bad input (e.g. a non-positive usage quantity)
// or a plan handle that isn't one of this integration's configured plans.
public class InvalidSubscriptionRequestException : Exception
{
    public InvalidSubscriptionRequestException(string message) : base(message)
    {
    }
}
