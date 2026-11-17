namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Strongly typed settings for the Finix integration. All values are supplied
/// through configuration (user-secrets / environment variables); no credential
/// value is ever committed to the repository.
/// </summary>
public class FinixSettings
{
    public const string SectionName = "Finix";
    public const string SandboxBaseUrl = "https://finix.sandbox-payments-api.com";
    public const string LiveBaseUrl = "https://finix.live-payments-api.com";
    public const string ApiVersion = "2022-02-01";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When present it is used verbatim for
    /// every Finix call instead of deriving an address from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase)
            ? LiveBaseUrl
            : SandboxBaseUrl;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(MerchantId);
}
