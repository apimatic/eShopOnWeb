using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IConfiguration config,
        ILogger<MaxioSubscriptionService> logger)
    {
        _logger = logger;
        _productFamilyHandle = config.GetValue<string>("Maxio:ProductFamilyHandle")
            ?? throw new InvalidOperationException("Maxio:ProductFamilyHandle not configured");

        var apiKey = config.GetValue<string>("Maxio:ApiKey")
            ?? throw new InvalidOperationException("Maxio:ApiKey not configured");

        var subdomain = config.GetValue<string>("Maxio:Subdomain")
            ?? throw new InvalidOperationException("Maxio:Subdomain not configured");

        var baseUrl = config.GetValue<string>("Maxio:BaseUrl");

        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = apiKey,
                Password = "x"
            },
            Environment = ServerEnvironment.Us
        };

        if (!string.IsNullOrEmpty(baseUrl))
        {
            options.Server.Production!.Us!.BaseUrl = baseUrl;
        }
        else
        {
            // Set the subdomain for the default production environment
            if (options.Server.Production?.Us != null)
            {
                options.Server.Production.Us.Site = subdomain;
            }
        }

        var httpClient = new HttpClient();
        _client = new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlans(CancellationToken ct)
    {
        try
        {
            var plans = new List<SubscriptionPlanDto>();

            var productResponses = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _productFamilyHandle,
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

            foreach (var response in productResponses)
            {
                if (response.Product != null)
                {
                    var priceInDollars = response.Product.PriceInCents.HasValue
                        ? (response.Product.PriceInCents.Value / 100m).ToString("0.00")
                        : "0.00";

                    plans.Add(new SubscriptionPlanDto
                    {
                        Id = response.Product.Id,
                        Name = response.Product.Name,
                        Handle = response.Product.Handle,
                        Description = response.Product.Description,
                        DefaultPrice = priceInDollars
                    });
                }
            }

            _logger.LogInformation("Retrieved {PlanCount} subscription plans from Maxio", plans.Count);
            return plans;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var error))
            {
                _logger.LogError("Maxio API returned 404: {Error}", error);
                throw new InvalidOperationException($"Product family not found: {error}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError("Maxio API error: HTTP {StatusCode}: {Body}", raw.StatusCode, raw.ReadAsString());
                throw new InvalidOperationException($"Failed to retrieve plans: {raw.StatusCode}", ex);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving subscription plans");
            throw new InvalidOperationException("Failed to retrieve subscription plans", ex);
        }
    }

    public async Task<SubscriptionPlanDto?> GetPlanByHandle(string planHandle, CancellationToken ct)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(
                apiHandle: planHandle,
                ct: ct);

            if (response.Product != null)
            {
                var priceInDollars = response.Product.PriceInCents.HasValue
                    ? (response.Product.PriceInCents.Value / 100m).ToString("0.00")
                    : "0.00";

                return new SubscriptionPlanDto
                {
                    Id = response.Product.Id,
                    Name = response.Product.Name,
                    Handle = response.Product.Handle,
                    Description = response.Product.Description,
                    DefaultPrice = priceInDollars
                };
            }

            return null;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Maxio API error retrieving plan {Handle}: HTTP {StatusCode}", planHandle, ex.Error.StatusCode);
            throw new InvalidOperationException($"Failed to retrieve plan: {ex.Error.StatusCode}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving plan {Handle}", planHandle);
            throw new InvalidOperationException("Failed to retrieve plan", ex);
        }
    }

    public async Task<int> GetOrCreateCustomer(string userEmail, string userId, CancellationToken ct)
    {
        try
        {
            // Search for existing customer by email
            var customerResponses = await _client.Customers.ListCustomers(
                direction: null,
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                q: userEmail,
                page: 1,
                perPage: 50,
                ct: ct);

            foreach (var response in customerResponses)
            {
                if (response.Customer?.Email == userEmail && response.Customer.Id.HasValue)
                {
                    _logger.LogInformation("Found existing Maxio customer for email {Email} with ID {CustomerId}", userEmail, response.Customer.Id);
                    return response.Customer.Id.Value;
                }
            }

            // Create new customer
            _logger.LogInformation("Creating new Maxio customer for email {Email}", userEmail);

            var createRequest = new MaxioAdvancedBilling.Models.CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    Email = userEmail,
                    FirstName = "User",
                    LastName = userId,
                    Reference = userId
                }
            };

            var createResponse = await _client.Customers.CreateCustomer(
                body: createRequest,
                ct: ct);

            if (createResponse.Customer?.Id.HasValue == true)
            {
                _logger.LogInformation("Created new Maxio customer with ID {CustomerId} for email {Email}",
                    createResponse.Customer.Id.Value, userEmail);
                return createResponse.Customer.Id.Value;
            }

            throw new InvalidOperationException("Customer creation returned no ID");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResp))
            {
                var errorMsg = string.Join("; ",
                    (errorResp.Errors?.PerPage ?? Enumerable.Empty<string>())
                    .Concat(errorResp.Errors?.PricePoint ?? Enumerable.Empty<string>()));
                _logger.LogError("Maxio validation error creating customer: {Error}", errorMsg);
                throw new InvalidOperationException($"Customer creation failed: {errorMsg}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError("Maxio API error creating customer: HTTP {StatusCode}: {Body}",
                    raw.StatusCode, raw.ReadAsString());
                throw new InvalidOperationException($"Failed to create customer: {raw.StatusCode}", ex);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting or creating customer for {Email}", userEmail);
            throw new InvalidOperationException("Failed to get or create customer", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscription(int customerId, string planHandle, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Creating subscription for customer {CustomerId} on plan {PlanHandle}",
                customerId, planHandle);

            var createRequest = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = planHandle
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: createRequest,
                ct: ct);

            if (response.Subscription != null)
            {
                _logger.LogInformation("Created subscription with ID {SubscriptionId} for customer {CustomerId}",
                    response.Subscription.Id, customerId);

                return MapToSubscriptionDto(response.Subscription);
            }

            throw new InvalidOperationException("Subscription creation returned no subscription object");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorResp))
            {
                var errorMsg = string.Join("; ", errorResp.Errors ?? Enumerable.Empty<string>());
                _logger.LogError("Maxio validation error creating subscription: {Error}", errorMsg);
                throw new InvalidOperationException($"Subscription creation failed: {errorMsg}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogError("Maxio API error creating subscription: HTTP {StatusCode}: {Body}",
                    raw.StatusCode, raw.ReadAsString());
                throw new InvalidOperationException($"Failed to create subscription: {raw.StatusCode}", ex);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating subscription for customer {CustomerId}", customerId);
            throw new InvalidOperationException("Failed to create subscription", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetCustomerSubscriptions(int customerId, CancellationToken ct)
    {
        try
        {
            var subscriptions = new List<SubscriptionDto>();

            var responses = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: ct);

            foreach (var response in responses)
            {
                if (response.Subscription != null)
                {
                    subscriptions.Add(MapToSubscriptionDto(response.Subscription));
                }
            }

            _logger.LogInformation("Retrieved {SubscriptionCount} subscriptions for customer {CustomerId}",
                subscriptions.Count, customerId);

            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError("Maxio API error retrieving subscriptions for customer {CustomerId}: HTTP {StatusCode}",
                customerId, ex.Error.StatusCode);
            throw new InvalidOperationException($"Failed to retrieve subscriptions: {ex.Error.StatusCode}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving subscriptions for customer {CustomerId}", customerId);
            throw new InvalidOperationException("Failed to retrieve subscriptions", ex);
        }
    }

    private static SubscriptionDto MapToSubscriptionDto(Subscription subscription)
    {
        var dto = new SubscriptionDto
        {
            Id = subscription.Id,
            CustomerId = subscription.Customer?.Id,
            ProductId = subscription.Product?.Id,
            State = subscription.State?.ToString() ?? "unknown",
            CurrentPeriodStartsAt = subscription.CurrentPeriodStartedAt,
            NextBillingAt = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt
        };

        return dto;
    }
}
