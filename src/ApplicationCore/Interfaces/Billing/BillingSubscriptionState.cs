namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

/// <summary>
/// Provider-agnostic subscription lifecycle state. The single Infrastructure billing-client
/// implementation maps the provider-specific state onto this set; nothing outside Infrastructure
/// ever sees a provider enum.
/// </summary>
public enum BillingSubscriptionState
{
    Unknown = 0,
    Trialing,
    Active,
    Paused,
    PastDue,
    Cancelled,
    Expired
}
