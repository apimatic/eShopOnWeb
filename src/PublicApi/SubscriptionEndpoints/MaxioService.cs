using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

public class MaxioService : IMaxioService
{
    private readonly MaxioClientFactory _clientFactory;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(
        MaxioClientFactory clientFactory,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioService> logger)
    {
        _clientFactory = clientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        var client = _clientFactory.GetClient();
        try
        {
            var input = new ListProductsInput
            {
                Page = 1,
                PerPage = 100
            };

            var responses = await client.ProductsController.ListProductsAsync(input);
            var plans = new List<SubscriptionPlanDto>();

            foreach (var resp in responses)
            {
                var product = resp.Product;
                if (product.ArchivedAt == null &&
                    product.ProductFamily?.Handle == _settings.ProductFamilyHandle)
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = product.Id ?? 0,
                        Handle = product.Handle ?? string.Empty,
                        Name = product.Name ?? string.Empty,
                        Description = product.Description ?? string.Empty,
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval ?? 0,
                        IntervalUnit = product.IntervalUnit?.ToString() ?? "month",
                        RequireCreditCard = product.RequireCreditCard ?? false
                    });
                }
            }

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Maxio products");
            throw;
        }
    }

    public async Task<SubscriptionPlanDto?> GetPlanByHandleAsync(string handle, CancellationToken ct = default)
    {
        var client = _clientFactory.GetClient();
        try
        {
            var response = await client.ProductsController.ReadProductByHandleAsync(handle);
            var product = response.Product;

            return new SubscriptionPlanDto
            {
                Id = product.Id ?? 0,
                Handle = product.Handle ?? string.Empty,
                Name = product.Name ?? string.Empty,
                Description = product.Description ?? string.Empty,
                PriceInCents = product.PriceInCents ?? 0,
                Interval = product.Interval ?? 0,
                IntervalUnit = product.IntervalUnit?.ToString() ?? "month",
                RequireCreditCard = product.RequireCreditCard ?? false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading Maxio product by handle: {Handle}", handle);
            return null;
        }
    }

    public async Task<CreateSubscriptionResult> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string firstName,
        string lastName,
        string email,
        CancellationToken ct = default)
    {
        var client = _clientFactory.GetClient();

        // Idempotent: find existing customer by reference first
        int? customerId = null;
        try
        {
            var existingCustomer = await client.CustomersController.ReadCustomerByReferenceAsync(customerReference);
            customerId = existingCustomer.Customer.Id;
            _logger.LogInformation("Found existing Maxio customer {CustomerId} for reference {Reference}", customerId, customerReference);

            // Check for existing active subscription on the same product (double-click protection)
            if (customerId.HasValue)
            {
                var existingSubscriptions = await client.CustomersController.ListCustomerSubscriptionsAsync(customerId.Value);
                var existingSub = existingSubscriptions.FirstOrDefault(s =>
                    s.Subscription.Product?.Handle == productHandle &&
                    s.Subscription.State?.ToString() == "Active");

                if (existingSub != null)
                {
                    _logger.LogInformation("Customer {Reference} already has active subscription {SubId} for product {ProductHandle}",
                        customerReference, existingSub.Subscription.Id, productHandle);
                    return new CreateSubscriptionResult
                    {
                        SubscriptionId = existingSub.Subscription.Id ?? 0,
                        State = existingSub.Subscription.State?.ToString() ?? "unknown",
                        CurrentPeriodEndsAt = existingSub.Subscription.CurrentPeriodEndsAt?.DateTime,
                        NextAssessmentAt = existingSub.Subscription.NextAssessmentAt?.DateTime,
                        ProductHandle = existingSub.Subscription.Product?.Handle ?? productHandle,
                        ProductName = existingSub.Subscription.Product?.Name ?? string.Empty,
                        PriceInCents = existingSub.Subscription.ProductPriceInCents ?? 0,
                        CustomerId = customerId.Value
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "No existing customer for reference {Reference}, will create", customerReference);
        }

        var subscription = new CreateSubscription
        {
            ProductHandle = productHandle,
            CustomerReference = customerReference,
            PaymentCollectionMethod = AdvancedBilling.Standard.Models.CollectionMethod.Remittance,
            CustomerAttributes = new CustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = customerReference
            }
        };

        var body = new CreateSubscriptionRequest
        {
            Subscription = subscription
        };

        try
        {
            var response = await client.SubscriptionsController.CreateSubscriptionAsync(body);
            var sub = response.Subscription;

            return new CreateSubscriptionResult
            {
                SubscriptionId = sub.Id ?? 0,
                State = sub.State?.ToString() ?? "unknown",
                CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt?.DateTime,
                NextAssessmentAt = sub.NextAssessmentAt?.DateTime,
                ProductHandle = sub.Product?.Handle ?? productHandle,
                ProductName = sub.Product?.Name ?? string.Empty,
                PriceInCents = sub.ProductPriceInCents ?? 0,
                CustomerId = sub.Customer?.Id ?? customerId ?? 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Maxio subscription for product {ProductHandle}, customer {Reference}", productHandle, customerReference);
            throw;
        }
    }

    public async Task<IReadOnlyList<MySubscriptionDto>> ListMySubscriptionsAsync(
        string customerReference,
        CancellationToken ct = default)
    {
        var client = _clientFactory.GetClient();

        int customerId;
        try
        {
            var customer = await client.CustomersController.ReadCustomerByReferenceAsync(customerReference);
            customerId = customer.Customer.Id ?? 0;
        }
        catch (Exception)
        {
            _logger.LogInformation("No customer found for reference {Reference}", customerReference);
            return Array.Empty<MySubscriptionDto>();
        }

        try
        {
            var subscriptions = await client.CustomersController.ListCustomerSubscriptionsAsync(customerId);

            return subscriptions.Select(s => new MySubscriptionDto
            {
                SubscriptionId = s.Subscription.Id ?? 0,
                State = s.Subscription.State?.ToString() ?? "unknown",
                CurrentPeriodEndsAt = s.Subscription.CurrentPeriodEndsAt?.DateTime,
                NextAssessmentAt = s.Subscription.NextAssessmentAt?.DateTime,
                ProductHandle = s.Subscription.Product?.Handle ?? string.Empty,
                ProductName = s.Subscription.Product?.Name ?? string.Empty,
                PriceInCents = s.Subscription.ProductPriceInCents ?? 0,
                CreatedAt = s.Subscription.CreatedAt?.DateTime
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions for customer {Reference}", customerReference);
            throw;
        }
    }
}
