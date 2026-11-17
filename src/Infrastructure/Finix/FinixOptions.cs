using System;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public sealed class FinixOptions
{
    public const string SectionName = "Finix";
    public const string SandboxBaseUrl = "https://finix.sandbox-payments-api.com";
    public const string LiveBaseUrl = "https://finix.live-payments-api.com";

    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? MerchantId { get; set; }
    public string? Environment { get; set; }
    public string? Currency { get; set; }
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        var environment = (Environment ?? string.Empty).Trim().ToLowerInvariant();
        return environment switch
        {
            "sandbox" => SandboxBaseUrl,
            "live" => LiveBaseUrl,
            "" => SandboxBaseUrl,
            _ => throw new InvalidOperationException(
                $"Unsupported Finix environment '{Environment}'. Expected 'sandbox' or 'live'.")
        };
    }

    public string RequireCurrency() =>
        string.IsNullOrWhiteSpace(Currency)
            ? throw new InvalidOperationException("Finix:Currency is not configured.")
            : Currency!.Trim().ToUpperInvariant();

    public string RequireMerchantId() =>
        string.IsNullOrWhiteSpace(MerchantId)
            ? throw new InvalidOperationException("Finix:MerchantId is not configured.")
            : MerchantId!.Trim();

    public void ValidateCredentials()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            throw new InvalidOperationException("Finix:Username and Finix:Password must be configured.");
        }

        RequireMerchantId();
        RequireCurrency();
    }
}
