namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A customer record in Maxio Advanced Billing, linked to an eShopOnWeb user
/// through the site-unique <see cref="Reference"/>.
/// </summary>
public class MaxioCustomer
{
    public long Id { get; set; }

    /// <summary>External id provided by this app (eShopOnWeb user reference).</summary>
    public string? Reference { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}
