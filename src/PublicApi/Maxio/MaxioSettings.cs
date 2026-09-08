using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSettings
{
    public const string CONFIG_SECTION_NAME = "Maxio";

    [Required]
    public string? ApiKey { get; set; }

    [Required]
    public string? Subdomain { get; set; }

    [Required]
    public string? ProductFamilyHandle { get; set; }

    public string? BaseUrl { get; set; }
}
