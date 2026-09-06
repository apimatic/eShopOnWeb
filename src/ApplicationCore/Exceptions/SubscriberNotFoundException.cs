using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The bearer token names a user that the identity store does not (or no longer) knows about.
/// </summary>
public class SubscriberNotFoundException : Exception
{
    public SubscriberNotFoundException(string userName)
        : base($"No eShopOnWeb user was found for '{userName}'.")
    {
        UserName = userName;
    }

    public string UserName { get; }
}
