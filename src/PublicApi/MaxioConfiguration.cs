using System;

namespace Microsoft.eShopWeb.PublicApi;

public sealed class MaxioConfiguration
{
    public string ApiKey { get; set; } = null!;
    public string Subdomain { get; set; } = null!;
    public string ProductFamilyHandle { get; set; } = null!;
    public string Environment { get; set; } = "us";
    public string? BaseUrl { get; set; }
}
