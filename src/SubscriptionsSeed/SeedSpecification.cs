using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// The shape UC0 provisions: one product family holding two recurring plans and one metered
/// component. Handles come from configuration so the seed always matches what the running
/// integration is configured to look for; the names and prices are the demo pricing model.
/// </summary>
public sealed class SeedSpecification
{
    private SeedSpecification(string familyHandle,
        PlanSpecification defaultPlan,
        PlanSpecification alternatePlan,
        ComponentSpecification meteredComponent)
    {
        FamilyHandle = familyHandle;
        FamilyName = "eShopSubscribe";
        DefaultPlan = defaultPlan;
        AlternatePlan = alternatePlan;
        MeteredComponent = meteredComponent;
    }

    public string FamilyHandle { get; }

    public string FamilyName { get; }

    public PlanSpecification DefaultPlan { get; }

    public PlanSpecification AlternatePlan { get; }

    public ComponentSpecification MeteredComponent { get; }

    public IEnumerable<PlanSpecification> Plans
    {
        get
        {
            yield return DefaultPlan;
            yield return AlternatePlan;
        }
    }

    public static SeedSpecification FromSettings(MaxioSettings settings)
    {
        var familyHandle = Require(settings.ProductFamilyHandle, nameof(settings.ProductFamilyHandle));

        return new SeedSpecification(
            familyHandle,
            // Pro Plan: $299.00/month, no trial, no setup fee, never expires, not taxable, and no
            // payment method required so the demo subscribes without card capture.
            new PlanSpecification(Require(settings.DefaultProductHandle, nameof(settings.DefaultProductHandle)),
                "Pro Plan", PriceInCents: 29_900),
            // Basic Plan: $29.00/month, same settings — the plan-change target for UC3.
            new PlanSpecification(Require(settings.AlternateProductHandle, nameof(settings.AlternateProductHandle)),
                "Basic Plan", PriceInCents: 2_900),
            // API Calls: metered, per-unit, $0.01/unit. It lives on the family, so it is available
            // to every subscription on either plan with no per-subscribe attach step.
            new ComponentSpecification(Require(settings.MeteredComponentHandle, nameof(settings.MeteredComponentHandle)),
                "API Calls", UnitName: "api call", UnitPrice: 0.01m));
    }

    private static string Require(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Maxio:{name} is not configured; the seed cannot run without it.")
            : value.Trim();
}

/// <summary>A recurring plan the seed provisions.</summary>
public sealed record PlanSpecification(string Handle, string Name, int PriceInCents)
{
    public int Interval => 1;

    public string IntervalUnit => "month";

    public bool RequiresPaymentMethod => false;

    public decimal Price => PriceInCents / 100m;
}

/// <summary>The metered component the seed provisions.</summary>
public sealed record ComponentSpecification(string Handle, string Name, string UnitName, decimal UnitPrice)
{
    public string PricingScheme => "per_unit";

    /// <summary>Maxio's kind for a metered component. A kind mismatch cannot be fixed in place.</summary>
    public string Kind => "metered_component";
}
