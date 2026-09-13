using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _settings;

    public SubscriptionService(IMaxioClient maxioClient, IOptions<MaxioSettings> settings)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetAvailablePlansAsync(CancellationToken ct = default)
    {
        var products = await _maxioClient.GetProductsByFamilyAsync(_settings.ProductFamilyHandle, ct);
        return products.Where(p => p.ArchivedAt == null).ToList();
    }

    public async Task<MaxioSubscription> SubscribeAsync(
        string userId, string email, string firstName, string lastName,
        string productHandle, CancellationToken ct = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(userId, ct);

        if (customer == null)
        {
            customer = await _maxioClient.CreateCustomerAsync(new MaxioCreateCustomerRequest
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userId
            }, ct);
        }

        // Idempotency: check for existing active subscription on the same product
        var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, ct);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            s.Product?.Handle == productHandle &&
            s.State is "active" or "trialing" or "pending");
        if (existing != null)
        {
            return existing;
        }

        var subscriptionRequest = new MaxioCreateSubscriptionRequest
        {
            ProductHandle = productHandle,
            CustomerId = customer.Id,
            PaymentCollectionMethod = "remittance"
        };

        return await _maxioClient.CreateSubscriptionAsync(subscriptionRequest, ct);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(userId, ct);
        if (customer == null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, ct);
        return subscriptions;
    }
}
