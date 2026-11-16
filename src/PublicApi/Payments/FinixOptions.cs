using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class FinixOptions
{
    public const string SectionName = "Finix";

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string MerchantId { get; set; } = string.Empty;

    [Required]
    public string Environment { get; set; } = string.Empty;

    [Required]
    public string Currency { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }
}
