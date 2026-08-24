using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Idempotently subscribes the user to a plan: finds-or-creates the Maxio customer
    /// (keyed on the username as the customer reference) and finds-or-creates the
    /// subscription (keyed on "{username}:{productHandle}").
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(string username, string email, string productHandle, CancellationToken ct = default);

    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken ct = default);
}
