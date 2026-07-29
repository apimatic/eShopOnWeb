namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A Maxio customer record mapped to an eShopOnWeb user via the stable
/// <see cref="Reference"/> (the eShopOnWeb user id).
/// </summary>
public class MaxioCustomer
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}
