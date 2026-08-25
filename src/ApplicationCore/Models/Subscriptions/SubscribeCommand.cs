namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// Everything the billing system needs to enroll a shopper in a plan.
/// </summary>
public class SubscribeCommand
{
    /// <summary>
    /// Stable, unique identifier of the shopper in eShopOnWeb (used as the billing customer reference).
    /// </summary>
    public string CustomerReference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
}
