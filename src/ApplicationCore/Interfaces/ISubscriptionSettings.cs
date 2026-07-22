namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic slice of configuration the domain needs. The concrete provider options
/// class in Infrastructure implements this, so ApplicationCore never depends outward (plan.md §2.2).
/// </summary>
public interface ISubscriptionSettings
{
    /// <summary>
    /// Handle of the metered component usage is billed against (UC2).
    /// </summary>
    string MeteredComponentHandle { get; }

    /// <summary>
    /// Handle of the plan offered as the default subscribe target (UC1).
    /// </summary>
    string DefaultProductHandle { get; }

    /// <summary>
    /// Handle of the second plan, used as the upgrade/downgrade target (UC3).
    /// </summary>
    string AlternateProductHandle { get; }
}
