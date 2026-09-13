using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Authentication;
using AdvancedBilling.Standard.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioService
{
    Task<List<PlanDto>> ListPlansAsync();
    Task<SubscriptionDto> SubscribeAsync(string productHandle, string customerReference, string firstName, string lastName, string email);
    Task<List<SubscriptionDto>> ListMySubscriptionsAsync(string customerReference);
}

public class MaxioService : IMaxioService
{
    private readonly AdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(IOptions<MaxioOptions> options, ILogger<MaxioService> logger)
    {
        _options = options.Value;
        _logger = logger;

        var builder = new AdvancedBillingClient.Builder()
            .BasicAuthCredentials(
                new BasicAuthModel.Builder(_options.ApiKey, "x").Build())
            .Environment(AdvancedBilling.Standard.Environment.US)
            .Site(_options.Subdomain);

        _client = builder.Build();
    }

    public async Task<List<PlanDto>> ListPlansAsync()
    {
        var input = new ListProductsForProductFamilyInput
        {
            ProductFamilyId = $"handle:{_options.ProductFamilyHandle}",
            PerPage = 100
        };

        var products = await _client.ProductFamiliesController.ListProductsForProductFamilyAsync(input);

        return products
            .Where(p => p.Product.ArchivedAt == null)
            .Select(p => new PlanDto
            {
                Id = p.Product.Id ?? 0,
                Handle = p.Product.Handle ?? string.Empty,
                Name = p.Product.Name ?? string.Empty,
                Description = p.Product.Description ?? string.Empty,
                PriceInCents = p.Product.PriceInCents ?? 0,
                Interval = p.Product.Interval ?? 0,
                IntervalUnit = p.Product.IntervalUnit?.ToString().ToLowerInvariant() ?? "month"
            })
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string productHandle, string customerReference, string firstName, string lastName, string email)
    {
        var existingCustomer = await FindOrCreateCustomerAsync(customerReference, firstName, lastName, email);

        var subscriptionRequest = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = existingCustomer.Id,
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        var result = await _client.SubscriptionsController.CreateSubscriptionAsync(subscriptionRequest);

        return MapSubscription(result.Subscription);
    }

    public async Task<List<SubscriptionDto>> ListMySubscriptionsAsync(string customerReference)
    {
        var customer = await FindCustomerByReferenceAsync(customerReference);
        if (customer == null)
        {
            return new List<SubscriptionDto>();
        }

        var customerId = customer.Id ?? 0;
        var subscriptions = await _client.CustomersController.ListCustomerSubscriptionsAsync(customerId);

        return subscriptions.Select(s => MapSubscription(s.Subscription)).ToList();
    }

    private async Task<Customer> FindOrCreateCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        var existing = await FindCustomerByReferenceAsync(reference);
        if (existing != null)
        {
            _logger.LogInformation("Found existing Maxio customer for reference {Reference}, id={Id}", reference, existing.Id);
            return existing;
        }

        _logger.LogInformation("Creating new Maxio customer for reference {Reference}", reference);
        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                Reference = reference,
                FirstName = firstName,
                LastName = lastName,
                Email = email
            }
        };

        var result = await _client.CustomersController.CreateCustomerAsync(createRequest);
        return result.Customer;
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var result = await _client.CustomersController.ReadCustomerByReferenceAsync(reference);
            return result.Customer;
        }
        catch (AdvancedBilling.Standard.Exceptions.ApiException ex) when (ex.ResponseCode == 404)
        {
            return null;
        }
    }

    private static SubscriptionDto MapSubscription(Subscription? sub)
    {
        if (sub == null)
        {
            return new SubscriptionDto();
        }

        return new SubscriptionDto
        {
            Id = sub.Id ?? 0,
            State = sub.State?.ToString().ToLowerInvariant() ?? "unknown",
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt?.DateTime,
            NextAssessmentAt = sub.NextAssessmentAt?.DateTime,
            CreatedAt = sub.CreatedAt?.DateTime ?? default,
            ProductName = sub.Product?.Name ?? string.Empty,
            ProductHandle = sub.Product?.Handle ?? string.Empty,
            PriceInCents = sub.ProductPriceInCents ?? 0
        };
    }
}
