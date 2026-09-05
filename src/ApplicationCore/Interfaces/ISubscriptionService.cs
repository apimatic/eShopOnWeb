using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<Subscription> SubscribeAsync(string userId, string email, string firstName, string lastName, string productHandle);
    Task<IEnumerable<Subscription>> GetUserSubscriptionsAsync(string userId);
}
