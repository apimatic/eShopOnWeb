using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, read from the "Maxio" configuration
/// section (e.g. <c>Maxio:ApiKey</c>) with fallback to the MAXIO_* environment variables.
/// Values are bound from configuration at startup; none are hard-coded.
/// </summary>
public sealed class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (from MAXIO_API_KEY).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain (from MAXIO_SITE_SUBDOMAIN).</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that holds the subscribable plans (from MAXIO_DEFAULT_PRODUCT_FAMILY).</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional verbatim API base-address override. When set it is used instead of deriving one from the subdomain.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Maxio environment: "us" (https://{site}.chargify.com) or "eu" (https://{site}.ebilling.maxio.com).</summary>
    public string Environment { get; set; } = "us";

    /// <summary>When true the Maxio HTTP traffic is written to the app log (never enabled in production).</summary>
    public bool LogTraffic { get; set; }

    public static MaxioOptions Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(CONFIG_NAME);
        return new MaxioOptions
        {
            ApiKey = FirstNonEmpty(section[nameof(ApiKey)], configuration["MAXIO_API_KEY"]) ?? string.Empty,
            Subdomain = FirstNonEmpty(section[nameof(Subdomain)], configuration["MAXIO_SITE_SUBDOMAIN"]) ?? string.Empty,
            ProductFamilyHandle = FirstNonEmpty(section[nameof(ProductFamilyHandle)], configuration["MAXIO_DEFAULT_PRODUCT_FAMILY"]) ?? string.Empty,
            BaseUrl = FirstNonEmpty(section[nameof(BaseUrl)], configuration["MAXIO_BASE_URL"]),
            Environment = FirstNonEmpty(section[nameof(Environment)], configuration["MAXIO_ENVIRONMENT"]) ?? "us",
            LogTraffic = bool.TryParse(FirstNonEmpty(section[nameof(LogTraffic)], configuration["MAXIO_LOG_TRAFFIC"]), out var logTraffic) && logTraffic
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }
}
