using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSettings
{
    [Required] public string ApiKey { get; init; } = string.Empty;
    [Required] public string Subdomain { get; init; } = string.Empty;
    [Required] public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }
}
