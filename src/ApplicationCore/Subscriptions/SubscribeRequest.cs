namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Input to <see cref="Interfaces.ISubscriptionBillingService.SubscribeAsync"/>.
/// The caller's identity (<see cref="UserReference"/>/<see cref="Email"/>) comes from the
/// authenticated token; the chosen plan comes from the request body.
/// </summary>
public class SubscribeRequest
{
    /// <summary>
    /// The eShopOnWeb user's stable identity. Used verbatim as the Maxio customer
    /// <c>reference</c>, which is the sole idempotency key for the customer.
    /// </summary>
    public string UserReference { get; set; } = string.Empty;

    /// <summary>Customer email (the eShopOnWeb user name is an email).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Optional first name; a value is derived from the email when not supplied.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional last name; a default is used when not supplied.</summary>
    public string? LastName { get; set; }

    /// <summary>The product handle of the plan to subscribe to.</summary>
    public string ProductHandle { get; set; } = string.Empty;
}
