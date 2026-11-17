using System;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public class FinixOptions
{
    public const string SectionName = "Finix";
    private const string SandboxBaseUrl = "https://finix.sandbox-payments-api.com";
    private const string LiveBaseUrl = "https://finix.live-payments-api.com";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "sandbox" => SandboxBaseUrl,
            "live" => LiveBaseUrl,
            _ => throw new InvalidOperationException(
                $"Finix:Environment must be 'sandbox' or 'live' but was '{Environment}'.")
        };
    }
}
