using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// The eShopOnWeb-side identity used to find or create the matching Maxio customer.
/// <see cref="Reference"/> must be a stable, unique string for the shopper (their Identity username/email).
/// </summary>
public record MaxioCustomerIdentity(string Reference, string Email, string FirstName, string LastName)
{
    /// <summary>
    /// eShopOnWeb's <c>ApplicationUser</c> carries no first/last name, only a username (email) -
    /// derive a presentable name from it so Maxio's required customer fields have something real.
    /// </summary>
    public static MaxioCustomerIdentity FromEShopUsername(string username)
    {
        var localPart = username.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart;
        return new MaxioCustomerIdentity(Reference: username, Email: username, FirstName: firstName, LastName: "Shopper");
    }
}

/// <summary>
/// A subscribable plan (Maxio "product") in the configured product family.
/// </summary>
public record SubscriptionPlanDto(string Handle, string Name, long? PriceInCents, string? IntervalUnit, int? Interval);

/// <summary>
/// A shopper's subscription to a plan, as currently recorded in Maxio.
/// </summary>
public record CustomerSubscriptionDto(
    int SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextAssessmentAt);
