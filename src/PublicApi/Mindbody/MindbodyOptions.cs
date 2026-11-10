namespace Microsoft.eShopWeb.PublicApi.Mindbody;

public class MindbodyOptions
{
    public const string CONFIG_NAME = "Mindbody";
    public const string DEFAULT_BASE_URL = "https://api.mindbodyonline.com/public/v6";

    public string? ApiKey { get; set; }
    public string? SiteId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Optional override of the API base address; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }

    public string EffectiveBaseUrl => string.IsNullOrWhiteSpace(BaseUrl) ? DEFAULT_BASE_URL : BaseUrl;
}
