using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class FinixSettings
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string MerchantId { get; set; } = string.Empty;

    public string Environment { get; set; } = "sandbox";

    public string Currency { get; set; } = "USD";

    public string? BaseUrl { get; set; }
}
