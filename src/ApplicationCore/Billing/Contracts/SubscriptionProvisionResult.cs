using Microsoft.eShopWeb.ApplicationCore.Billing.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Billing.Contracts;

public sealed record SubscriptionProvisionResult(
    MaxioCustomer Customer,
    MaxioSubscription Subscription,
    bool Created);
