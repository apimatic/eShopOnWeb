namespace Microsoft.eShopWeb.Maxio.Contracts;

/// <summary>
/// Describes the shopper that is subscribing and the plan they want.
/// </summary>
public sealed class SubscribeCommand
{
    /// <summary>The Maxio customer reference that uniquely identifies the shopper (their local user id).</summary>
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>Shopper email; used as the Maxio customer email when the customer is first created.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Optional given name used when the Maxio customer is first created.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional family name used when the Maxio customer is first created.</summary>
    public string? LastName { get; set; }

    /// <summary>Handle of the plan (Maxio product) to subscribe to.</summary>
    public string ProductHandle { get; set; } = string.Empty;
}
