using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _maxio;
    private readonly IConfiguration _config;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient maxio,
        IConfiguration config,
        ILogger<MaxioSubscriptionService> logger)
    {
        _maxio = maxio;
        _config = config;
        _logger = logger;
    }

    public async Task<(Customer customer, bool isNew)> GetOrCreateCustomerAsync(
        string customerReference, string firstName, string lastName, string email, CancellationToken ct)
    {
        try
        {
            var existing = await _maxio.Customers.ReadCustomerByReference(
                reference: customerReference, ct: ct);

            _logger.LogInformation("Found existing Maxio customer for reference {Reference}", customerReference);
            return (existing.Customer!, false);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Customer not found for reference {Reference}, creating new one", customerReference);

            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = customerReference
                }
            };

            var response = await _maxio.Customers.CreateCustomer(body: createRequest, ct: ct);
            _logger.LogInformation("Created new Maxio customer {CustomerId} for reference {Reference}",
                response.Customer?.Id, customerReference);

            return (response.Customer!, true);
        }
    }

    public async Task<Subscription> CreateSubscriptionAsync(
        int customerId, string productHandle, string? subscriptionReference, CancellationToken ct)
    {
        var createRequest = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                Reference = subscriptionReference,
                DeferSignup = false
            }
        };

        var response = await _maxio.Subscriptions.CreateSubscription(body: createRequest, ct: ct);

        _logger.LogInformation(
            "Created subscription {SubscriptionId} for customer {CustomerId} on product {ProductHandle}",
            response.Subscription?.Id, customerId, productHandle);

        return response.Subscription!;
    }

    public async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken ct)
    {
        var response = await _maxio.Customers.ListCustomerSubscriptions(
            customerId: customerId, ct: ct);

        var subscriptions = response
            .Select(r => r.Subscription!)
            .Where(s => s != null)
            .ToList();

        _logger.LogInformation("Listed {Count} subscriptions for customer {CustomerId}",
            subscriptions.Count, customerId);

        return subscriptions;
    }

    public async Task<IReadOnlyList<Product>> ListSubscriptionProductsAsync(CancellationToken ct)
    {
        var productFamilyHandle = _config["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";

        var products = new List<Product>();

        var response = await _maxio.ProductFamilies.ListProductsForProductFamily(
            productFamilyId: productFamilyHandle,
            dateField: null,
            filter: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            includeArchived: null,
            include: null,
            page: 1,
            perPage: 100,
            ct: ct);

        foreach (var productResponse in response)
        {
            if (productResponse.Product != null)
            {
                products.Add(productResponse.Product);
            }
        }

        _logger.LogInformation("Listed {Count} products from family {FamilyHandle}",
            products.Count, productFamilyHandle);

        return products;
    }
}
