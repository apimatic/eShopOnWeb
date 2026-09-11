using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Authentication;
using AdvancedBilling.Standard.Exceptions;
using AdvancedBilling.Standard.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

public class MaxioService : IMaxioService
{
    private readonly AdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(
        IOptions<MaxioSettings> settings,
        ILogger<MaxioService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var environment = _settings.Subdomain.Contains("-eu", StringComparison.OrdinalIgnoreCase)
            ? AdvancedBilling.Standard.Environment.EU
            : AdvancedBilling.Standard.Environment.US;

        _client = new AdvancedBillingClient.Builder()
            .BasicAuthCredentials(
                new BasicAuthModel.Builder(_settings.ApiKey, "x").Build())
            .Site(_settings.Subdomain)
            .Environment(environment)
            .Build();
    }

    public async Task<IReadOnlyList<MaxioPlanInfo>> ListPlansAsync()
    {
        _logger.LogInformation("Listing plans for product family: {Handle}", _settings.ProductFamilyHandle);

        var input = new ListProductsForProductFamilyInput(
            productFamilyId: $"handle:{_settings.ProductFamilyHandle}",
            perPage: 100);

        var products = await _client.ProductFamiliesController.ListProductsForProductFamilyAsync(input);

        return products
            .Where(p => p.Product.ArchivedAt == null)
            .Select(p => MapToPlanInfo(p.Product))
            .ToList();
    }

    public async Task<MaxioPlanInfo?> GetPlanByHandleAsync(string handle)
    {
        _logger.LogInformation("Getting plan by handle: {Handle}", handle);

        try
        {
            var response = await _client.ProductsController.ReadProductByHandleAsync(handle);
            return MapToPlanInfo(response.Product);
        }
        catch (ApiException ex) when (ex.ResponseCode == 404)
        {
            return null;
        }
    }

    public async Task<MaxioCustomerInfo> FindOrCreateCustomerAsync(
        string reference, string email, string firstName, string lastName)
    {
        _logger.LogInformation("Finding or creating Maxio customer for reference: {Reference}", reference);

        // Try to find existing customer by reference (idempotent)
        try
        {
            var existing = await _client.CustomersController.ReadCustomerByReferenceAsync(reference);
            _logger.LogInformation("Found existing Maxio customer {Id} for reference {Reference}", existing.Customer.Id, reference);
            return MapToCustomerInfo(existing.Customer);
        }
        catch (ApiException ex) when (ex.ResponseCode == 404)
        {
            _logger.LogInformation("No existing customer found for reference {Reference}, creating new one", reference);
        }

        // Create new customer
        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference,
            }
        };

        var response = await _client.CustomersController.CreateCustomerAsync(createRequest);
        _logger.LogInformation("Created Maxio customer {Id} for reference {Reference}", response.Customer.Id, reference);
        return MapToCustomerInfo(response.Customer);
    }

    public async Task<MaxioSubscriptionInfo> CreateOrFindSubscriptionAsync(
        int customerId,
        string productHandle,
        string? productPricePointHandle = null)
    {
        _logger.LogInformation("Creating subscription for customer {CustomerId} on product {ProductHandle}", customerId, productHandle);

        // Check for existing active subscription on this product for this customer (idempotency)
        try
        {
            var existingSubscriptions = await _client.CustomersController.ListCustomerSubscriptionsAsync(customerId);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                s.Subscription.Product.Handle == productHandle &&
                (s.Subscription.State == SubscriptionState.Active || s.Subscription.State == SubscriptionState.Trialing));

            if (existing != null)
            {
                _logger.LogInformation("Found existing subscription {Id} for customer {CustomerId} on product {ProductHandle}",
                    existing.Subscription.Id, customerId, productHandle);
                return MapToSubscriptionInfo(existing.Subscription);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking existing subscriptions for customer {CustomerId}, proceeding with creation", customerId);
        }

        // Create new subscription
        var createSubscription = new CreateSubscription
        {
            ProductHandle = productHandle,
            CustomerId = customerId,
            PaymentCollectionMethod = CollectionMethod.Remittance,
        };

        if (!string.IsNullOrEmpty(productPricePointHandle))
        {
            createSubscription.ProductPricePointHandle = productPricePointHandle;
        }

        var createRequest = new CreateSubscriptionRequest
        {
            Subscription = createSubscription,
        };

        try
        {
            var response = await _client.SubscriptionsController.CreateSubscriptionAsync(createRequest);
            _logger.LogInformation("Created subscription {Id} for customer {CustomerId}",
                response.Subscription.Id, customerId);
            return MapToSubscriptionInfo(response.Subscription);
        }
        catch (ErrorListResponseException errorEx)
        {
            _logger.LogError(errorEx, "Error creating subscription for customer {CustomerId} on product {ProductHandle}",
                customerId, productHandle);
            throw;
        }
    }

    public async Task<IReadOnlyList<MaxioSubscriptionInfo>> ListCustomerSubscriptionsAsync(int customerId)
    {
        _logger.LogInformation("Listing subscriptions for customer {CustomerId}", customerId);

        var subscriptions = await _client.CustomersController.ListCustomerSubscriptionsAsync(customerId);

        return subscriptions
            .Select(s => MapToSubscriptionInfo(s.Subscription))
            .ToList();
    }

    public async Task<MaxioSubscriptionInfo?> ReadSubscriptionAsync(int subscriptionId)
    {
        _logger.LogInformation("Reading subscription {SubscriptionId}", subscriptionId);

        try
        {
            var response = await _client.SubscriptionsController.ReadSubscriptionAsync(subscriptionId);
            return MapToSubscriptionInfo(response.Subscription);
        }
        catch (ApiException ex) when (ex.ResponseCode == 404)
        {
            return null;
        }
    }

    private static MaxioPlanInfo MapToPlanInfo(Product product)
    {
        return new MaxioPlanInfo
        {
            Id = product.Id ?? 0,
            Name = product.Name ?? string.Empty,
            Handle = product.Handle ?? string.Empty,
            Description = product.Description ?? string.Empty,
            PriceInCents = product.PriceInCents ?? 0,
            IntervalUnit = product.IntervalUnit?.ToString() ?? "month",
            Interval = product.Interval ?? 1,
            RequireCreditCard = product.RequireCreditCard ?? false,
            ProductFamilyHandle = product.ProductFamily?.Handle,
            ProductPricePointName = product.ProductPricePointName,
        };
    }

    private static MaxioCustomerInfo MapToCustomerInfo(Customer customer)
    {
        return new MaxioCustomerInfo
        {
            Id = customer.Id ?? 0,
            FirstName = customer.FirstName ?? string.Empty,
            LastName = customer.LastName ?? string.Empty,
            Email = customer.Email ?? string.Empty,
            Reference = customer.Reference,
        };
    }

    private static MaxioSubscriptionInfo MapToSubscriptionInfo(Subscription subscription)
    {
        return new MaxioSubscriptionInfo
        {
            Id = subscription.Id ?? 0,
            State = subscription.State?.ToString() ?? "unknown",
            ProductId = subscription.Product?.Id,
            ProductName = subscription.Product?.Name,
            ProductHandle = subscription.Product?.Handle,
            ProductPriceInCents = subscription.Product?.PriceInCents,
            CustomerId = subscription.Customer?.Id,
            CustomerEmail = subscription.Customer?.Email,
            CustomerFirstName = subscription.Customer?.FirstName,
            CustomerLastName = subscription.Customer?.LastName,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt?.UtcDateTime,
            NextAssessmentAt = subscription.NextAssessmentAt?.UtcDateTime,
            ActivatedAt = subscription.ActivatedAt?.UtcDateTime,
            CreatedAt = subscription.CreatedAt?.UtcDateTime,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod?.ToString(),
        };
    }
}
