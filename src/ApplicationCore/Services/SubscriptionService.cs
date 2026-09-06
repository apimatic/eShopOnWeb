using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;
using LocalSubscription = Microsoft.eShopWeb.ApplicationCore.Entities.Subscription;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IRepository<LocalSubscription> _subscriptionRepository;

    public SubscriptionService(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        IRepository<LocalSubscription> subscriptionRepository)
    {
        _settings = settings.Value;
        _subscriptionRepository = subscriptionRepository;
        _client = InitializeClient(httpClient);
    }

    private MaxioAdvancedBillingClient InitializeClient(HttpClient httpClient)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials
            {
                Username = _settings.ApiKey ?? "",
                Password = "x"
            },
            Environment = ServerEnvironment.Us,
            Server = new ServerOptions
            {
                Production = new ProductionOptions
                {
                    Us = new ProductionOptions.UsOptions
                    {
                        Site = _settings.Subdomain ?? ""
                    }
                }
            }
        };

        if (!string.IsNullOrEmpty(_settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = _settings.BaseUrl;
        }

        return new MaxioAdvancedBillingClient(httpClient, options);
    }

    public async Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _settings.ProductFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 20,
                ct: ct);

            return response.Select(p => new SubscriptionPlanDto
            {
                Id = p.Product?.Id ?? 0,
                Handle = p.Product?.Handle ?? "",
                Name = p.Product?.Name ?? "",
                PriceInCents = p.Product?.PriceInCents ?? 0,
                Interval = p.Product?.Interval ?? 1,
                IntervalUnit = p.Product?.IntervalUnit?.ToString() ?? "month"
            }).ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            // Case A error with string accessor for 404 or other statuses
            if (ex.Error.TryGetString(out var errorMessage))
            {
                throw new InvalidOperationException($"Failed to list products: Product family not found or access denied", ex);
            }
            if (ex.Error.TryGetRawError(out var rawError))
            {
                throw new InvalidOperationException($"Failed to list products: {rawError.ReadAsString()}", ex);
            }
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to list subscription plans: {ex.Message}", ex);
        }
    }

    public async Task<SubscriptionCustomerDto> GetOrCreateCustomerAsync(
        string email,
        string firstName,
        string lastName,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            var existingCustomer = await _client.Customers.ReadCustomerByReference(
                reference: email,
                ct: ct);

            if (existingCustomer?.Customer != null)
            {
                return new SubscriptionCustomerDto
                {
                    MaxioCustomerId = existingCustomer.Customer.Id ?? 0,
                    Email = existingCustomer.Customer.Email ?? "",
                    FirstName = existingCustomer.Customer.FirstName ?? "",
                    LastName = existingCustomer.Customer.LastName ?? ""
                };
            }
        }
        catch (SdkException<RawError> ex)
        {
            // Case B error: Check if customer not found (404)
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                // Customer doesn't exist, will create a new one
                return await CreateCustomerAsync(email, firstName, lastName, userId, ct);
            }
            // Other errors should be re-thrown
            throw new InvalidOperationException($"Failed to lookup customer: {ex.Error.ReadAsString()}", ex);
        }

        return await CreateCustomerAsync(email, firstName, lastName, userId, ct);
    }

    private async Task<SubscriptionCustomerDto> CreateCustomerAsync(
        string email,
        string firstName,
        string lastName,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            var createRequest = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = userId
                }
            };

            var response = await _client.Customers.CreateCustomer(
                body: createRequest,
                ct: ct);

            return new SubscriptionCustomerDto
            {
                MaxioCustomerId = response.Customer?.Id ?? 0,
                Email = response.Customer?.Email ?? "",
                FirstName = response.Customer?.FirstName ?? "",
                LastName = response.Customer?.LastName ?? ""
            };
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // Case A error with CustomerErrorResponse1 accessor for 422 status
            if (ex.Error.TryGetCustomerErrorResponse1(out var customerError))
            {
                var errors = string.Join(", ", customerError.Errors?.PerPage ?? new List<string>());
                throw new InvalidOperationException($"Failed to create customer: {errors}", ex);
            }
            if (ex.Error.TryGetRawError(out var rawError))
            {
                throw new InvalidOperationException($"Failed to create customer: {rawError.ReadAsString()}", ex);
            }
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create customer: {ex.Message}", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle
                }
            };

            var response = await _client.Subscriptions.CreateSubscription(
                body: createRequest,
                ct: ct);

            if (response.Subscription != null)
            {
                await _subscriptionRepository.AddAsync(new LocalSubscription
                {
                    UserId = userId,
                    MaxioCustomerId = customerId,
                    MaxioSubscriptionId = response.Subscription.Id ?? 0,
                    ProductHandle = productHandle,
                    ProductName = response.Subscription.Product?.Name ?? "",
                    PriceInCents = response.Subscription.ProductPriceInCents ?? 0,
                    State = response.Subscription.State?.ToString() ?? "active",
                    ActivatedAt = response.Subscription.ActivatedAt,
                    NextAssessmentAt = response.Subscription.NextAssessmentAt,
                    CurrentPeriodEndsAt = response.Subscription.CurrentPeriodEndsAt,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });

                return MapToDto(response.Subscription);
            }

            throw new InvalidOperationException("Failed to create subscription: empty response");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // Case A error with ErrorListResponse1 accessor for 422 status
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var errors = errorList.Errors ?? new List<string>();

                // Check if subscription already exists for this customer/product
                if (errors.Any(e => e.Contains("already exists", StringComparison.OrdinalIgnoreCase)))
                {
                    return await GetExistingSubscriptionAsync(customerId, productHandle, ct);
                }

                throw new InvalidOperationException($"Failed to create subscription: {string.Join(", ", errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var rawError))
            {
                throw new InvalidOperationException($"Failed to create subscription: {rawError.ReadAsString()}", ex);
            }
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create subscription: {ex.Message}", ex);
        }
    }

    private async Task<SubscriptionDto> GetExistingSubscriptionAsync(
        int customerId,
        string productHandle,
        CancellationToken ct = default)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: ct);

            var existing = subscriptions.FirstOrDefault(s =>
                s.Subscription?.Product?.Handle == productHandle);

            if (existing?.Subscription != null)
            {
                return MapToDto(existing.Subscription);
            }

            throw new InvalidOperationException("Subscription already exists but could not be retrieved");
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to retrieve existing subscription: {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to retrieve existing subscription: {ex.Message}", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken ct = default)
    {
        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(
                customerId: customerId,
                ct: ct);

            return subscriptions
                .Where(s => s.Subscription != null)
                .Select(s => MapToDto(s.Subscription!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to list subscriptions: {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to list subscriptions: {ex.Message}", ex);
        }
    }

    private static SubscriptionDto MapToDto(MaxioSubscription? maxioSub)
    {
        if (maxioSub == null)
            throw new InvalidOperationException("Subscription is null");

        return new SubscriptionDto
        {
            Id = maxioSub.Id ?? 0,
            ProductHandle = maxioSub.Product?.Handle ?? "",
            ProductName = maxioSub.Product?.Name ?? "",
            PriceInCents = maxioSub.ProductPriceInCents ?? 0,
            State = maxioSub.State?.ToString() ?? "active",
            ActivatedAt = maxioSub.ActivatedAt,
            NextAssessmentAt = maxioSub.NextAssessmentAt,
            CurrentPeriodEndsAt = maxioSub.CurrentPeriodEndsAt
        };
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "";
}

public class SubscriptionCustomerDto
{
    public int MaxioCustomerId { get; set; }
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = "";
    public string ProductName { get; set; } = "";
    public long PriceInCents { get; set; }
    public string State { get; set; } = "";
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
