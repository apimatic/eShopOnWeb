namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A Maxio customer, keyed in Maxio by our application user id as <c>reference</c>.
/// </summary>
public class BillingCustomer
{
    public int Id { get; init; }
    public string? Reference { get; init; }
    public string Email { get; init; } = string.Empty;
}
