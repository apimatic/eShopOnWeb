using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvancedBilling.Standard;
using AdvancedBilling.Standard.Exceptions;
using AdvancedBilling.Standard.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

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
        try
        {
            var input = new ListProductsForProductFamilyInput
            {
                ProductFamilyId = $"handle:{_options.ProductFamilyHandle}",
                PerPage = 100
            };

            var products = await _client.ProductFamiliesController.ListProductsForProductFamilyAsync(input, CancellationToken.None);

            return products
                .Where(p => p.Product?.ArchivedAt == null)
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Product.Id ?? 0,
                    Name = p.Product.Name,
                    Handle = p.Product.Handle,
                    Description = p.Product.Description ?? string.Empty,
                    PriceInCents = p.Product.PriceInCents ?? 0,
                    IntervalUnit = p.Product.IntervalUnit?.ToString() ?? string.Empty,
                    Interval = p.Product.Interval ?? 0,
                    ProductFamilyHandle = p.Product.ProductFamily?.Handle ?? _options.ProductFamilyHandle
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans from Maxio");
            throw;
        }
    }

    public async Task<SubscriptionResultDto> SubscribeAsync(string userEmail, string? userFirstName, string? userLastName, string productHandle)
    {
        try
        {
            var customer = await GetOrCreateCustomerAsync(userEmail, userFirstName, userLastName);

            var subscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customer.Id ?? 0,
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            };

            var result = await _client.SubscriptionsController.CreateSubscriptionAsync(subscriptionRequest, CancellationToken.None);

            var sub = result.Subscription;
            return new SubscriptionResultDto
            {
                SubscriptionId = sub.Id ?? 0,
                State = sub.State?.ToString() ?? string.Empty,
                ProductHandle = sub.Product?.Handle ?? productHandle,
                ProductName = sub.Product?.Name ?? productHandle,
                PriceInCents = sub.Product?.PriceInCents ?? 0,
                CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt?.DateTime,
                NextAssessmentAt = sub.NextAssessmentAt?.DateTime,
                CustomerId = customer.Id ?? 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for {Email} with product {Handle}", userEmail, productHandle);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userEmail)
    {
        try
        {
            var customer = await FindCustomerByEmailAsync(userEmail);
            if (customer == null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            var customerId = customer.Id ?? 0;
            var subscriptions = await _client.CustomersController.ListCustomerSubscriptionsAsync(customerId, CancellationToken.None);

            return subscriptions
                .Select(s => new SubscriptionDto
                {
                    Id = s.Subscription.Id ?? 0,
                    State = s.Subscription.State?.ToString() ?? string.Empty,
                    ProductHandle = s.Subscription.Product?.Handle ?? string.Empty,
                    ProductName = s.Subscription.Product?.Name ?? string.Empty,
                    PriceInCents = s.Subscription.Product?.PriceInCents ?? 0,
                    CurrentPeriodEndsAt = s.Subscription.CurrentPeriodEndsAt?.DateTime,
                    NextAssessmentAt = s.Subscription.NextAssessmentAt?.DateTime,
                    ActivatedAt = s.Subscription.ActivatedAt?.DateTime,
                    CanceledAt = s.Subscription.CanceledAt?.DateTime,
                    CancellationMessage = s.Subscription.CancellationMessage
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for {Email}", userEmail);
            throw;
        }
    }

    private async Task<Customer> GetOrCreateCustomerAsync(string email, string? firstName, string? lastName)
    {
        var existing = await FindCustomerByEmailAsync(email);
        if (existing != null)
        {
            return existing;
        }

        var createRequest = new CreateCustomerRequest
        {
            Customer = new AdvancedBilling.Standard.Models.CreateCustomer
            {
                FirstName = firstName ?? email.Split('@')[0],
                LastName = lastName ?? "",
                Email = email,
                Reference = email
            }
        };

        var response = await _client.CustomersController.CreateCustomerAsync(createRequest, CancellationToken.None);
        return response.Customer;
    }

    private async Task<Customer?> FindCustomerByEmailAsync(string email)
    {
        try
        {
            var result = await _client.CustomersController.ReadCustomerByReferenceAsync(email, CancellationToken.None);
            return result.Customer;
        }
        catch (ApiException)
        {
            return null;
        }
    }
}
