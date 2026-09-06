namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The authenticated eShopOnWeb shopper on whose behalf we talk to the billing system.
/// </summary>
/// <param name="UserName">
/// The user name carried by the caller's JWT. This is the durable key: it survives application
/// restarts and database resets, which locally generated identity ids do not.
/// </param>
/// <param name="Email">Address to put on the billing record. Falls back to the user name when blank.</param>
/// <param name="FirstName">Optional override for the billing record's first name.</param>
/// <param name="LastName">Optional override for the billing record's last name.</param>
public sealed record SubscriberIdentity(string UserName, string? Email = null, string? FirstName = null, string? LastName = null)
{
    /// <summary>The value stored as the billing customer's <c>reference</c>, our idempotency key.</summary>
    public string BillingReference => BillingReferences.ForUser(UserName);

    public string EmailAddress => string.IsNullOrWhiteSpace(Email) ? UserName : Email!;
}
