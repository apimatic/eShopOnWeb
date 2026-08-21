using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task EnsureCustomerAsync(BillingUser user, string customerReference, CancellationToken cancellationToken);
    Task<SubscriptionConfirmation?> FindSubscriptionAsync(
        string subscriptionReference,
        string expectedCustomerReference,
        string expectedProductHandle,
        CancellationToken cancellationToken);
    Task<SubscriptionConfirmation> CreateSubscriptionAsync(
        string subscriptionReference,
        string customerReference,
        string productHandle,
        CancellationToken cancellationToken);
}

