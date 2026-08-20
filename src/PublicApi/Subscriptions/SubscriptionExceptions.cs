using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionValidationException : Exception
{
    public SubscriptionValidationException(string message) : base(message) { }
}

public sealed class AuthenticatedUserNotFoundException : Exception
{
    public AuthenticatedUserNotFoundException() : base("The authenticated user no longer exists.") { }
}
