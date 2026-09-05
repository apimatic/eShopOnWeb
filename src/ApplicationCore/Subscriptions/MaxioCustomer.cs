namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A Maxio Customer. <see cref="Reference"/> holds the eShopOnWeb user name, which is how
/// we look the customer up idempotently instead of persisting our own id mapping.
/// </summary>
public class MaxioCustomer
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
