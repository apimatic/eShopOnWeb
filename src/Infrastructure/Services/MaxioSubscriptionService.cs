using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IOptions<MaxioOptions> options, ILogger<MaxioSubscriptionService> logger)
    {
        _options = options.Value;
        _logger = logger;

        var httpClient = new HttpClient();
        var sdkOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = _options.ApiKey,
                Password = "x"
            }
        };

        if (!string.IsNullOrEmpty(_options.BaseUrl))
        {
            sdkOptions.Server.Production.Us.BaseUrl = _options.BaseUrl;
        }
        else
        {
            sdkOptions.Server.Production.Us.Site = _options.Subdomain;
        }

        _client = new MaxioAdvancedBillingClient(httpClient, sdkOptions);
    }

    public async Task<IEnumerable<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _options.ProductFamilyHandle,
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

            return products
                .Where(p => p.Product != null)
                .Select(p => new SubscriptionPlanDto(
                    Id: p.Product!.Id ?? 0,
                    Handle: p.Product.Handle ?? string.Empty,
                    Name: p.Product.Name ?? string.Empty,
                    PriceInCents: p.Product.PriceInCents ?? 0))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Failed to list subscription plans: HTTP {(int)ex.Error.StatusCode} - {ex.Error.ReadAsString()}");
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError($"Failed to deserialize subscription plans response: {ex.Message}");
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string userReference,
        string userEmail,
        string firstName,
        string lastName,
        string productHandle,
        CancellationToken ct = default)
    {
        try
        {
            var customer = await FindOrCreateCustomerAsync(userReference, userEmail, firstName, lastName, ct);

            var subscriptionRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customer.Id,
                    ProductHandle = productHandle,
                    Reference = $"{userReference}-{productHandle}-{DateTime.UtcNow.Ticks}"
                }
            };

            var subscriptionResponse = await _client.Subscriptions.CreateSubscription(subscriptionRequest, ct: ct);
            var subscription = subscriptionResponse.Subscription;

            if (subscription == null)
            {
                throw new InvalidOperationException("Subscription creation returned null response");
            }

            return new SubscriptionDto(
                Id: subscription.Id ?? 0,
                Reference: subscription.Reference,
                State: subscription.State?.Value ?? "unknown",
                ProductPriceInCents: subscription.ProductPriceInCents,
                CurrentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
                NextAssessmentAt: subscription.NextAssessmentAt);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Failed to create subscription: HTTP {(int)ex.Error.StatusCode} - {ex.Error.ReadAsString()}");
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError($"Failed to deserialize subscription response: {ex.Message}");
            throw;
        }
    }

    public async Task<IEnumerable<SubscriptionDto>> GetUserSubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        try
        {
            var customer = await ReadCustomerByReferenceAsync(userReference, ct);

            if (customer == null)
            {
                return Enumerable.Empty<SubscriptionDto>();
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                customerId: customer.Id ?? 0,
                ct: ct);

            return subscriptions
                .Where(s => s.Subscription != null)
                .Select(s => new SubscriptionDto(
                    Id: s.Subscription!.Id ?? 0,
                    Reference: s.Subscription.Reference,
                    State: s.Subscription.State?.Value ?? "unknown",
                    ProductPriceInCents: s.Subscription.ProductPriceInCents,
                    CurrentPeriodEndsAt: s.Subscription.CurrentPeriodEndsAt,
                    NextAssessmentAt: s.Subscription.NextAssessmentAt))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Failed to list user subscriptions: HTTP {(int)ex.Error.StatusCode} - {ex.Error.ReadAsString()}");
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError($"Failed to deserialize subscriptions response: {ex.Message}");
            throw;
        }
    }

    private async Task<Customer> FindOrCreateCustomerAsync(
        string reference,
        string email,
        string firstName,
        string lastName,
        CancellationToken ct)
    {
        var existingCustomer = await ReadCustomerByReferenceAsync(reference, ct);
        if (existingCustomer != null)
        {
            return existingCustomer;
        }

        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(createRequest, ct: ct);
            var customer = response.Customer ?? throw new InvalidOperationException("Customer creation returned null");
            _logger.LogInformation($"Created new Maxio customer {customer.Id} for reference {reference}");
            return customer;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Failed to create customer: HTTP {(int)ex.Error.StatusCode} - {ex.Error.ReadAsString()}");
            throw;
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation($"Customer with reference {reference} not found");
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError($"Failed to read customer by reference: HTTP {(int)ex.Error.StatusCode} - {ex.Error.ReadAsString()}");
            throw;
        }
    }
}
