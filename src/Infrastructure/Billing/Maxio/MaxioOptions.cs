using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Which eShopOnWeb value is used as the <c>reference</c> on the Maxio customer record. The
/// reference is the join key between an eShopOnWeb user and their Maxio customer, so it has to be
/// stable for the lifetime of the account.
/// </summary>
public enum MaxioCustomerReferenceSource
{
    /// <summary>
    /// Use the (lower-cased) email / user name. Stable across restarts even when the identity
    /// store is the in-memory provider, which re-generates user ids on every run. This is the
    /// default because it keeps the mapping correct in every supported hosting configuration.
    /// </summary>
    Email = 0,

    /// <summary>
    /// Use the ASP.NET Identity user id. Preferable when the identity store is durable and users
    /// are allowed to change their email address.
    /// </summary>
    UserId = 1
}

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the <c>Maxio</c> configuration
/// section. Nothing here has a value baked into the repository: credentials and site/catalog
/// coordinates all come from configuration (user-secrets or environment variables).
/// </summary>
public class MaxioOptions
{
    /// <summary>Name of the configuration section these options bind from.</summary>
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio API key. Sent as the Basic-auth user name with the password <c>x</c>, per the
    /// <c>BasicAuth</c> security scheme in the specification.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ApiKey is required.")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Subdomain of the Maxio site. Substituted into the <c>site</c> server variable of the
    /// specification to build the base address.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family whose products are offered as subscription plans.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Maxio:ProductFamilyHandle is required.")]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit base address. When set it is used verbatim and both
    /// <see cref="Subdomain"/> and <see cref="Environment"/> are ignored for URL construction.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Hosting environment of the Maxio site, matching the <c>x-server-configuration</c>
    /// environments in the specification: <c>US</c> (<c>https://{site}.chargify.com</c>) or
    /// <c>EU</c> (<c>https://{site}.ebilling.maxio.com</c>). Ignored when <see cref="BaseUrl"/> is
    /// set. Defaults to the <c>default-environment</c> of the specification, US.
    /// </summary>
    public string Environment { get; set; } = UsEnvironment;

    /// <summary>
    /// Optional plan handle used when a subscribe request does not name one. Left empty by
    /// default so the catalog is never hard-coded into the build.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Collection-Method requested when creating a subscription (<c>automatic</c>,
    /// <c>remittance</c>, <c>prepaid</c> or <c>invoice</c>).
    /// <para>
    /// Leave empty to let the integration pick: it reads <c>GET /site.json</c> and asks for
    /// <c>remittance</c> on a Relationship Invoicing site or <c>invoice</c> on a legacy Statements
    /// site. Both mean "bill by invoice", which is the only collection method that can succeed
    /// while this integration captures no payment method. Set it explicitly to override.
    /// </para>
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>How long the site configuration is cached in memory. It changes very rarely.</summary>
    [Range(0, 86_400)]
    public int SiteCacheSeconds { get; set; } = 900;

    /// <summary>Which eShopOnWeb value is used as the Maxio customer reference.</summary>
    public MaxioCustomerReferenceSource CustomerReferenceSource { get; set; } = MaxioCustomerReferenceSource.Email;

    /// <summary>
    /// Prefix applied to every reference we write to Maxio, so records created by this
    /// application are recognisable on a shared site.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ReferencePrefix { get; set; } = "eshoponweb";

    /// <summary>Per-request timeout, in seconds.</summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How many times a transient failure is retried before giving up.</summary>
    [Range(0, 10)]
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for the exponential retry backoff, in milliseconds.</summary>
    [Range(0, 60_000)]
    public int RetryBaseDelayMilliseconds { get; set; } = 250;

    /// <summary>
    /// How long the plan catalog is cached in memory. The catalog changes rarely and every
    /// subscribe validates against it, so a short cache removes a round-trip per call.
    /// </summary>
    [Range(0, 3600)]
    public int PlanCacheSeconds { get; set; } = 60;

    /// <summary>How long a single subscribe attempt waits for the per-subscriber lock.</summary>
    [Range(1, 300)]
    public int SubscribeLockTimeoutSeconds { get; set; } = 30;

    internal const string UsEnvironment = "US";
    internal const string EuEnvironment = "EU";

    /// <summary>
    /// Resolves the API base address. <see cref="BaseUrl"/> wins when present; otherwise the
    /// server template of the specification for the configured environment is filled in with
    /// <see cref="Subdomain"/>.
    /// </summary>
    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(EnsureTrailingSlash(BaseUrl.Trim()), UriKind.Absolute, out var explicitUri))
            {
                throw new InvalidOperationException(
                    $"Maxio:BaseUrl must be an absolute URL. Value provided: '{BaseUrl}'.");
            }

            return explicitUri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        var host = ResolveHostTemplate().Replace("{site}", Subdomain.Trim(), StringComparison.Ordinal);
        return new Uri(EnsureTrailingSlash(host), UriKind.Absolute);
    }

    private string ResolveHostTemplate() => Environment?.Trim().ToUpperInvariant() switch
    {
        EuEnvironment => "https://{site}.ebilling.maxio.com",
        UsEnvironment or null or "" => "https://{site}.chargify.com",
        _ => throw new InvalidOperationException(
            $"Maxio:Environment must be '{UsEnvironment}' or '{EuEnvironment}'. Value provided: '{Environment}'.")
    };

    /// <summary>The Collection-Method enum values defined by the specification.</summary>
    internal static readonly string[] CollectionMethods = { "automatic", "remittance", "prepaid", "invoice" };

    private static string EnsureTrailingSlash(string value) =>
        value.EndsWith('/') ? value : value + "/";
}
