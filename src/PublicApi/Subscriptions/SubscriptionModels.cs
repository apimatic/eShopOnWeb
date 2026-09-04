using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    string? PricePointHandle,
    string? PricePointName)
{
    // Provider identifiers are deliberately kept out of the public JSON contract.
    // They are selected dynamically from the current handle lookup because Maxio
    // can reassign numeric IDs when a sandbox catalog is re-seeded.
    internal int? ProductId { get; init; }
    internal int? PricePointId { get; init; }
}

public sealed record SubscriptionDto(
    int SubscriptionId,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    long? CurrentBillingAmountInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? NextAssessmentDate);

public sealed class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public sealed record CurrentUserIdentity(
    string Email,
    string FirstName,
    string LastName,
    string Reference)
{
    public static CurrentUserIdentity? From(System.Security.Claims.ClaimsPrincipal principal)
    {
        var email = principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var localPart = normalizedEmail.Split('@')[0];
        var nameParts = localPart.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var firstName = principal.FindFirst("given_name")?.Value
            ?? (nameParts.Length > 0 ? nameParts[0] : "eShop");
        var lastName = principal.FindFirst("family_name")?.Value
            ?? (nameParts.Length > 1 ? nameParts[^1] : "User");

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(normalizedEmail))).ToLowerInvariant();

        return new CurrentUserIdentity(
            normalizedEmail,
            firstName,
            lastName,
            $"eshop-user:{hash}");
    }
}

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        System.Net.HttpStatusCode statusCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public System.Net.HttpStatusCode StatusCode { get; }
}
