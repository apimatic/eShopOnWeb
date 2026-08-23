using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string Subdomain { get; set; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }
}
