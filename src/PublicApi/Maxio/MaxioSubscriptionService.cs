using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Exceptions;
using AdvancedBilling.Standard.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly AdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        AdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync()
    {
        var input = new ListProductsInput
        {
            PerPage = 100,
        };

        var products = await _client.ProductsController.ListProductsAsync(input);

        return products
            .Where(p => p.Product?.ArchivedAt == null)
            .Where(p => string.Equals(
                p.Product?.ProductFamily?.Handle,
                _options.ProductFamilyHandle,
                StringComparison.OrdinalIgnoreCase))
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Product.Id ?? 0,
                Name = p.Product.Name ?? string.Empty,
                Handle = p.Product.Handle ?? string.Empty,
                Description = p.Product.Description ?? string.Empty,
                PriceInCents = (int)(p.Product.PriceInCents ?? 0),
                IntervalUnit = p.Product.IntervalUnit?.ToString() ?? "month",
                Interval = p.Product.Interval ?? 1,
                ProductFamilyHandle = p.Product.ProductFamily?.Handle ?? string.Empty,
            })
            .ToList();
    }

    public async Task<CreateSubscriptionResult> CreateSubscriptionAsync(
        string userEmail,
        string firstName,
        string lastName,
        string productHandle,
        string? customerReference = null)
    {
        var reference = customerReference ?? userEmail;

        // Idempotent customer creation: find existing by reference, or create new
        var customerId = await FindOrCreateCustomerAsync(userEmail, firstName, lastName, reference);

        // Create subscription using product handle
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = CollectionMethod.Remittance,
            },
        };

        _logger.LogInformation(
            "Creating Maxio subscription for customer {CustomerId} with product {ProductHandle}",
            customerId, productHandle);

        var result = await _client.SubscriptionsController.CreateSubscriptionAsync(body);
        var sub = result.Subscription;

        return new CreateSubscriptionResult
        {
            SubscriptionId = sub?.Id ?? 0,
            State = sub?.State?.ToString() ?? "unknown",
            CustomerId = sub?.Customer?.Id ?? customerId,
            ProductName = sub?.Product?.Name ?? string.Empty,
            ProductHandle = sub?.Product?.Handle ?? productHandle,
            ProductPriceInCents = (int)(sub?.Product?.PriceInCents ?? 0),
            CurrentPeriodEndsAt = sub?.CurrentPeriodEndsAt?.UtcDateTime,
            NextAssessmentAt = sub?.NextAssessmentAt?.UtcDateTime,
            CreatedAt = sub?.CreatedAt?.UtcDateTime ?? DateTime.UtcNow,
        };
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string customerReference)
    {
        // Look up the customer by reference
        var customer = await FindCustomerByReferenceAsync(customerReference);
        if (customer?.Customer == null)
        {
            _logger.LogWarning("No Maxio customer found with reference {Reference}", customerReference);
            return Array.Empty<SubscriptionDto>();
        }

        var customerId = customer.Customer.Id ?? 0;
        var subscriptions = await _client.CustomersController.ListCustomerSubscriptionsAsync(customerId);

        return subscriptions
            .Select(s => new SubscriptionDto
            {
                SubscriptionId = s.Subscription?.Id ?? 0,
                State = s.Subscription?.State?.ToString() ?? "unknown",
                ProductName = s.Subscription?.Product?.Name ?? string.Empty,
                ProductHandle = s.Subscription?.Product?.Handle ?? string.Empty,
                ProductPriceInCents = (int)(s.Subscription?.Product?.PriceInCents ?? 0),
                CurrentPeriodStartsAt = s.Subscription?.CurrentPeriodStartedAt?.UtcDateTime,
                CurrentPeriodEndsAt = s.Subscription?.CurrentPeriodEndsAt?.UtcDateTime,
                NextAssessmentAt = s.Subscription?.NextAssessmentAt?.UtcDateTime,
                CanceledAt = s.Subscription?.CanceledAt?.UtcDateTime,
                CreatedAt = s.Subscription?.CreatedAt?.UtcDateTime ?? DateTime.UtcNow,
            })
            .ToList();
    }

    private async Task<int> FindOrCreateCustomerAsync(
        string email, string firstName, string lastName, string reference)
    {
        // Try to find existing customer by reference first
        var existing = await FindCustomerByReferenceAsync(reference);
        if (existing?.Customer?.Id != null)
        {
            _logger.LogInformation("Found existing Maxio customer {CustomerId} for reference {Reference}", existing.Customer.Id, reference);
            return existing.Customer.Id.Value;
        }

        // Create new customer
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference,
            },
        };

        _logger.LogInformation("Creating Maxio customer for {Email} with reference {Reference}", email, reference);
        var result = await _client.CustomersController.CreateCustomerAsync(body);
        return result.Customer?.Id ?? 0;
    }

    private async Task<CustomerResponse?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var result = await _client.CustomersController.ReadCustomerByReferenceAsync(reference);
            return result;
        }
        catch (ApiException ex) when (ex.ResponseCode == 404)
        {
            return null;
        }
    }
}
