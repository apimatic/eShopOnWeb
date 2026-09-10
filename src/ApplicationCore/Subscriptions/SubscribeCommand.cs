namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Application-level request to subscribe an eShopOnWeb user to a plan.
/// <paramref name="ProductHandle"/> may be null/empty, in which case the service falls back to the
/// first plan advertised by the configured product family.
/// </summary>
public record SubscribeCommand(CustomerRegistration Customer, string? ProductHandle);
