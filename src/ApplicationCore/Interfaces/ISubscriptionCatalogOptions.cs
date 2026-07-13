namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

// The subset of the Maxio configuration (plan §5) that SubscriptionService needs to validate
// requests against. ApplicationCore depends on nothing outward, so it defines this small seam
// rather than referencing Infrastructure's MaxioSettings directly; MaxioSettings implements it.
public interface ISubscriptionCatalogOptions
{
    string DefaultProductHandle { get; }
    string AlternateProductHandle { get; }
    string MeteredComponentHandle { get; }
}
