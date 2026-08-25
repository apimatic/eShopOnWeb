namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identity of an eShopOnWeb user as known to the billing system.
/// <see cref="Reference"/> is the stable, unique key shared by both systems.
/// </summary>
public class SubscriberInfo
{
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}
