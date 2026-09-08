namespace Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;

/// <summary>
/// A Maxio customer record, linked back to eShopOnWeb through <see cref="Reference"/>.
/// </summary>
public class MaxioCustomer
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
}
