using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to create a new subscription
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    public string UserReference { get; set; } = string.Empty;

    [Required]
    public string ProductHandle { get; set; } = string.Empty;

    [Required]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
