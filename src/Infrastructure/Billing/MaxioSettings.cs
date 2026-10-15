using System;
using System.Collections.Generic;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio Advanced Billing settings, bound from the <c>Maxio:</c> configuration section.
/// Values come from user-secrets in development and from the environment (<c>Maxio__ApiKey</c>, …) when deployed;
/// none of them belongs in an appsettings file.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary><c>Maxio:ApiKey</c> — the site API key (Basic auth user name). Required.</summary>
    public string? ApiKey { get; init; }

    /// <summary><c>Maxio:Subdomain</c> — the Maxio site subdomain. Required unless <see cref="BaseUrl"/> is set.</summary>
    public string? Subdomain { get; init; }

    /// <summary><c>Maxio:ProductFamilyHandle</c> — the product family whose products are offered as plans. Required.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary><c>Maxio:BaseUrl</c> — optional; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; init; }

    /// <summary><c>Maxio:Environment</c> — optional hosting region, <c>US</c> (default) or <c>EU</c>.</summary>
    public string? Environment { get; init; }

    /// <summary>
    /// <c>Maxio:PaymentCollectionMethod</c> — optional; how new subscriptions are collected. eShopOnWeb captures no
    /// payment details, so the default is <c>remittance</c> (invoiced; Relationship Invoicing sites). A site on the legacy
    /// Statements architecture sets <c>invoice</c>.
    /// </summary>
    public string? PaymentCollectionMethod { get; init; }

    public CollectionMethod CollectionMethod =>
        CollectionMethod.TryGetKnownValue(PaymentCollectionMethod?.Trim().ToLowerInvariant() ?? string.Empty, out var known)
            ? known
            : CollectionMethod.Remittance;

    public static MaxioSettings FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new MaxioSettings
        {
            ApiKey = section["ApiKey"],
            Subdomain = section["Subdomain"],
            ProductFamilyHandle = section["ProductFamilyHandle"],
            BaseUrl = section["BaseUrl"],
            Environment = section["Environment"],
            PaymentCollectionMethod = section["PaymentCollectionMethod"],
        };
    }

    public bool IsEuEnvironment => string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lists every configuration problem by key name. Never echoes a value.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add("Maxio:ApiKey is not configured.");
        }
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            problems.Add("Maxio:ProductFamilyHandle is not configured.");
        }
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (string.IsNullOrWhiteSpace(Subdomain))
            {
                problems.Add("Maxio:Subdomain is not configured (required unless Maxio:BaseUrl is set).");
            }
        }
        else if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var baseUri)
                 || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            problems.Add("Maxio:BaseUrl must be an absolute http(s) URL.");
        }
        if (!string.IsNullOrWhiteSpace(Environment)
            && !string.Equals(Environment.Trim(), "US", StringComparison.OrdinalIgnoreCase)
            && !IsEuEnvironment)
        {
            problems.Add("Maxio:Environment must be 'US' or 'EU'.");
        }
        if (!string.IsNullOrWhiteSpace(PaymentCollectionMethod)
            && !CollectionMethod.TryGetKnownValue(PaymentCollectionMethod.Trim().ToLowerInvariant(), out _))
        {
            problems.Add("Maxio:PaymentCollectionMethod must be one of 'remittance', 'invoice', 'automatic' or 'prepaid'.");
        }
        return problems;
    }
}
