using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Strongly-typed view of the <c>Maxio:</c> configuration section. Values are supplied by the
/// host (user-secrets in development, environment variables or a secret store elsewhere) and are
/// never checked into the repository.
/// </summary>
public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key. Sent as the basic-auth user name.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Maxio site subdomain, used to build the API base address.</summary>
    public string? Subdomain { get; init; }

    /// <summary>
    /// Optional override. When set it is used verbatim as the API base address and the subdomain
    /// is not substituted into anything.
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>
    /// Optional handle of the plan to use when a subscribe request does not name one. Left unset,
    /// the API requires callers to choose a plan explicitly rather than guessing for them.
    /// </summary>
    public string? DefaultProductHandle { get; init; }

    /// <summary>
    /// Optional override for how the provider collects payment: <c>automatic</c>,
    /// <c>remittance</c>, <c>invoice</c> or <c>prepaid</c>.
    /// <para>
    /// Left unset, the collection method is derived per plan: a plan the provider reports as not
    /// requiring a payment method is subscribed with <c>remittance</c>, because the site default
    /// (<c>automatic</c>) rejects a signup that has no card on file. Set this only to force a
    /// specific method for every plan.
    /// </para>
    /// </summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>Bound on a single HTTP attempt against Maxio.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Bound on a whole operation, including every retry the SDK performs.</summary>
    public TimeSpan CallBudget { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>How long the plan catalogue is cached for. Zero disables caching.</summary>
    public TimeSpan PlanCacheDuration { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Logs one line per HTTP request/response against Maxio (method, path, status, elapsed).
    /// Off by default; credentials and bodies are never logged.
    /// </summary>
    public bool LogHttpTraffic { get; init; }

    public static MaxioSettings Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        return new MaxioSettings
        {
            ApiKey = Trimmed(section["ApiKey"]),
            Subdomain = Trimmed(section["Subdomain"]),
            BaseUrl = Trimmed(section["BaseUrl"]),
            ProductFamilyHandle = Trimmed(section["ProductFamilyHandle"]),
            DefaultProductHandle = Trimmed(section["DefaultProductHandle"]),
            PaymentCollectionMethod = Trimmed(section["PaymentCollectionMethod"]),
            AttemptTimeout = Seconds(section["AttemptTimeoutSeconds"], 15),
            CallBudget = Seconds(section["CallBudgetSeconds"], 45),
            PlanCacheDuration = Seconds(section["PlanCacheSeconds"], 60),
            LogHttpTraffic = Flag(section["LogHttpTraffic"])
        };
    }

    /// <summary>
    /// Returns one message per configuration problem; an empty result means the integration can
    /// serve requests.
    /// </summary>
    public IReadOnlyCollection<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            problems.Add(SectionName + ":ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            problems.Add(SectionName + ":ProductFamilyHandle is not configured.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            problems.Add("Either " + SectionName + ":Subdomain or " + SectionName + ":BaseUrl must be configured.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) && !Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            problems.Add(SectionName + ":BaseUrl is not an absolute URL.");
        }

        if (!string.IsNullOrWhiteSpace(PaymentCollectionMethod) &&
            !SupportedCollectionMethods.Contains(PaymentCollectionMethod!.Trim().ToLowerInvariant()))
        {
            problems.Add(SectionName + ":PaymentCollectionMethod must be one of " +
                string.Join(", ", SupportedCollectionMethods) + ".");
        }

        return problems;
    }

    /// <summary>Collection methods the provider accepts, as wire values.</summary>
    internal static readonly IReadOnlyCollection<string> SupportedCollectionMethods =
        new[] { "automatic", "remittance", "invoice", "prepaid" };

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TimeSpan Seconds(string? value, int fallbackSeconds)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
        {
            return TimeSpan.FromSeconds(parsed);
        }

        return TimeSpan.FromSeconds(fallbackSeconds);
    }

    private static bool Flag(string? value) => bool.TryParse(value, out var parsed) && parsed;
}
