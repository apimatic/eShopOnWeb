using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioBillingService
{
    Task<List<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);
    Task<(bool Success, string Message, UserSubscription? Subscription)> CreateSubscriptionAsync(
        string userId,
        string firstName,
        string lastName,
        string email,
        string productHandle,
        CancellationToken cancellationToken = default);
    Task<List<UserSubscription>> GetUserSubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
