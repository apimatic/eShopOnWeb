using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly IMaxioHttpClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioHttpClient maxio,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<MaxioProduct>> GetAvailablePlansAsync()
    {
        return await _maxio.ListProductsForFamilyAsync(_options.ProductFamilyHandle);
    }

    public async Task<MaxioSubscription> SubscribeAsync(
        string userReference, string userFirstName, string userLastName, string userEmail, string productHandle)
    {
        // Idempotent customer lookup
        var customer = await _maxio.LookupCustomerByReferenceAsync(userReference);
        if (customer == null)
        {
            _logger.LogInformation("Creating Maxio customer for reference {Reference}", userReference);
            customer = await _maxio.CreateCustomerAsync(new MaxioCreateCustomerRequest
            {
                Customer = new MaxioCreateCustomer
                {
                    FirstName = userFirstName,
                    LastName = userLastName,
                    Email = userEmail,
                    Reference = userReference
                }
            });
        }
        else
        {
            _logger.LogInformation("Found existing Maxio customer {CustomerId} for reference {Reference}", customer.Id, userReference);
        }

        // Create subscription
        _logger.LogInformation("Creating subscription for customer {CustomerId}, product {ProductHandle}", customer.Id, productHandle);
        var subscription = await _maxio.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = "remittance"
            }
        });

        return subscription;
    }

    public async Task<List<MaxioSubscription>> GetMySubscriptionsAsync(string userReference)
    {
        var customer = await _maxio.LookupCustomerByReferenceAsync(userReference);
        if (customer == null)
        {
            _logger.LogInformation("No Maxio customer found for reference {Reference}", userReference);
            return new List<MaxioSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id);
    }
}
