using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps the domain subscription types onto the API DTOs, and resolves the caller's billing
/// identity from their authenticated user name.
/// </summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Name = plan.Name,
        Handle = plan.Handle,
        PriceInCents = plan.PriceInCents,
        FormattedPrice = plan.FormattedPrice,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        Description = plan.Description
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.ProductHandle,
        PlanName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        FormattedPrice = subscription.FormattedPrice,
        NextBillingDate = subscription.NextBillingDate,
        CustomerReference = subscription.CustomerReference
    };

    /// <summary>
    /// Builds the billing identity from the authenticated user name. In eShopOnWeb the JWT
    /// <c>name</c> claim is the user's email, which we use as the stable Maxio customer reference.
    /// First/last name are derived from the email local part (Maxio requires both to create a
    /// customer, and eShopOnWeb's identity model does not store them).
    /// </summary>
    public static SubscriberInfo ToSubscriber(string userName)
    {
        var email = userName;
        var (first, last) = DeriveName(email);
        return new SubscriberInfo(Reference: email, Email: email, FirstName: first, LastName: last);
    }

    private static (string First, string Last) DeriveName(string email)
    {
        var local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var first = parts.Length > 0 ? Capitalize(parts[0]) : "eShopOnWeb";
        var last = parts.Length > 1 ? Capitalize(parts[^1]) : "eShopOnWeb";
        return (first, last);
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
