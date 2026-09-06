using System;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>Shapes taken from real Maxio sandbox responses, trimmed to the fields we map.</summary>
internal static class MaxioTestData
{
    public const string UserName = "demouser@microsoft.com";
    public const string CustomerReference = "eshoponweb:demouser@microsoft.com";
    public const string ProPlanHandle = "demo-pro";
    public const string BasicPlanHandle = "demo-basic";
    public const string FamilyHandle = "demo-family";

    public static IOptions<MaxioSettings> Settings(Action<MaxioSettings>? configure = null)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle
        };

        configure?.Invoke(settings);
        return Options.Create(settings);
    }

    public static MaxioSite Site(string currency = "USD") =>
        new() { Id = 93062, Subdomain = "test-site", Currency = currency, Test = true };

    public static MaxioProduct Product(
        string handle = ProPlanHandle,
        string name = "Pro Plan",
        long priceInCents = 29900,
        DateTimeOffset? archivedAt = null) =>
        new()
        {
            Id = 7130997,
            Handle = handle,
            Name = name,
            PriceInCents = priceInCents,
            Interval = 1,
            IntervalUnit = "month",
            RequireCreditCard = false,
            ArchivedAt = archivedAt,
            ProductFamily = new MaxioProductFamily { Id = 3026730, Handle = FamilyHandle, Name = "Demo Family" }
        };

    public static MaxioCustomer Customer(long id = 98837075, string reference = CustomerReference) =>
        new()
        {
            Id = id,
            Reference = reference,
            Email = UserName,
            FirstName = "Demouser",
            LastName = "Microsoft"
        };

    public static MaxioSubscription Subscription(
        long id = 94208179,
        string state = "active",
        string planHandle = ProPlanHandle,
        string? reference = null) =>
        new()
        {
            Id = id,
            State = state,
            Reference = reference,
            BalanceInCents = 29900,
            ProductPriceInCents = 29900,
            Currency = "USD",
            CurrentPeriodStartedAt = new DateTimeOffset(2026, 9, 6, 9, 36, 49, TimeSpan.FromHours(5)),
            CurrentPeriodEndsAt = new DateTimeOffset(2026, 10, 6, 9, 36, 49, TimeSpan.FromHours(5)),
            NextAssessmentAt = new DateTimeOffset(2026, 10, 6, 9, 36, 49, TimeSpan.FromHours(5)),
            ActivatedAt = new DateTimeOffset(2026, 9, 6, 9, 36, 50, TimeSpan.FromHours(5)),
            PaymentCollectionMethod = "remittance",
            Product = Product(planHandle),
            Customer = Customer()
        };

    /// <summary>The 422 Maxio answers with when an application-chosen reference is already in use.</summary>
    public static MaxioApiException ReferenceTaken(string method, string path) =>
        new(System.Net.HttpStatusCode.UnprocessableEntity, method, path,
            new[] { "Reference: must be unique - that value has been taken." });
}
