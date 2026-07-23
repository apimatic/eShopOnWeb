namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-neutral slice of billing configuration the domain needs: which plan handles the
/// storefront offers and which component metered usage accrues to. Declared here — and implemented by the
/// Infrastructure options class — so <see cref="ISubscriptionService"/> can enforce the UC2 and UC3 rules
/// without ApplicationCore taking a dependency on configuration or on the provider (plan.md §2.2).
/// </summary>
public interface ISubscriptionSettings
{
    /// <summary>Handle of the plan the storefront offers by default.</summary>
    string DefaultProductHandle { get; }

    /// <summary>Handle of the second plan, the target for an upgrade or downgrade (plan.md UC3).</summary>
    string AlternateProductHandle { get; }

    /// <summary>Handle of the metered component that pay-as-you-go usage accrues to (plan.md UC2).</summary>
    string MeteredComponentHandle { get; }
}
