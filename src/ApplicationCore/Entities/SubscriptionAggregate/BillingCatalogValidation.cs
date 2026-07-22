using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The result of checking that the billing provider's catalog matches what this integration is
/// configured to use (plan.md UC0 / UC2 preconditions): the product family resolves, the configured
/// plan handles resolve, and the configured usage component exists and really is metered.
/// <para>
/// This is a report, not an exception: the caller decides whether an invalid catalog blocks a page
/// (a friendly error) or a write (a refusal). Nothing here throws, so a provider outage during
/// validation never takes a host down.
/// </para>
/// </summary>
public class BillingCatalogValidation
{
    public BillingCatalogValidation(string productFamilyHandle,
        int? productFamilyId,
        IReadOnlyList<SubscriptionPlan> plans,
        IReadOnlyList<string> errors,
        bool isMeteredComponentValid,
        int? meteredComponentId = null,
        string? meteredComponentKind = null)
    {
        ProductFamilyHandle = productFamilyHandle;
        ProductFamilyId = productFamilyId;
        Plans = plans;
        Errors = errors;
        IsMeteredComponentValid = isMeteredComponentValid;
        MeteredComponentId = meteredComponentId;
        MeteredComponentKind = meteredComponentKind;
    }

    public string ProductFamilyHandle { get; }

    /// <summary>The live numeric id the configured family handle resolved to, or null if it did not resolve.</summary>
    public int? ProductFamilyId { get; }

    /// <summary>The plans found in the configured product family.</summary>
    public IReadOnlyList<SubscriptionPlan> Plans { get; }

    /// <summary>Human-readable reasons the catalog does not match the configuration. Empty when valid.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True only when the configured usage component exists and is of metered kind.</summary>
    public bool IsMeteredComponentValid { get; }

    public int? MeteredComponentId { get; }

    /// <summary>The kind the provider reports for the configured component, for diagnostics when it is wrong.</summary>
    public string? MeteredComponentKind { get; }

    public bool IsValid => Errors.Count == 0;
}
