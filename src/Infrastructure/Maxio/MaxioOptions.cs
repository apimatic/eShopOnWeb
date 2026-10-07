using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }

    public string? Subdomain { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public string? BaseUrl { get; set; }

    public string? Environment { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public int RequestTimeoutSeconds { get; set; } = 30;

    public int PlanCacheSeconds { get; set; } = 60;

    public string EffectivePaymentCollectionMethod =>
        string.IsNullOrWhiteSpace(PaymentCollectionMethod) ? "remittance" : PaymentCollectionMethod.Trim().ToLowerInvariant();

    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.Trim(), UriKind.Absolute);
        }

        var host = string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase)
            ? $"https://{Subdomain!.Trim()}.ebilling.maxio.com"
            : $"https://{Subdomain!.Trim()}.chargify.com";
        return new Uri(host, UriKind.Absolute);
    }
}

public class MaxioOptionsValidator : IValidateOptions<MaxioOptions>
{
    private static readonly string[] AllowedCollectionMethods = { "automatic", "remittance", "prepaid" };

    public ValidateOptionsResult Validate(string? name, MaxioOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Maxio:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            failures.Add("Maxio:ProductFamilyHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(options.Subdomain))
            {
                failures.Add("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
            }
        }
        else if (!Uri.TryCreate(options.BaseUrl.Trim(), UriKind.Absolute, out var uri)
                 || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Maxio:BaseUrl must be an absolute http(s) URL.");
        }

        if (Array.IndexOf(AllowedCollectionMethods, options.EffectivePaymentCollectionMethod) < 0)
        {
            failures.Add("Maxio:PaymentCollectionMethod must be one of: automatic, remittance, prepaid.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
