namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Payload used to create the Maxio customer that represents an eShopOnWeb user.
/// </summary>
public class CreateMaxioCustomerRequest
{
    /// <summary>
    /// Site-unique external id (the eShopOnWeb user reference). A second create
    /// with the same reference is rejected by the billing system, which keeps
    /// customer provisioning idempotent.
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Organization { get; set; }
}
