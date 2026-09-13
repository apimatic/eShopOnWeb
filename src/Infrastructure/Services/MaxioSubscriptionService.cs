using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService : ISubscriptionService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<PlanDto>> GetPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Products.ListProducts(
                dateField: null,
                filter: null,
                endDate: null,
                endDatetime: null,
                startDate: null,
                startDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 50,
                ct: ct);

            return response
                .Where(pr => pr.Product != null && pr.Product.ProductFamily?.Handle == _settings.ProductFamilyHandle)
                .Select(pr => new PlanDto
                {
                    Id = pr.Product!.Id ?? 0,
                    Name = pr.Product.Name ?? "",
                    Handle = pr.Product.Handle ?? "",
                    Description = pr.Product.Description ?? "",
                    PriceInDollars = (pr.Product.PriceInCents ?? 0) / 100.0m,
                    IntervalUnit = pr.Product.IntervalUnit?.Value ?? "",
                    Interval = pr.Product.Interval ?? 0
                })
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException(
                $"Failed to list Maxio products: HTTP {(int)ex.Error.StatusCode}", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string userReference, string email, string firstName, string lastName,
        string productHandle, CancellationToken ct = default)
    {
        int customerId = await EnsureCustomerExistsAsync(userReference, email, firstName, lastName, ct);

        try
        {
            var subResp = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customerId,
                        PaymentCollectionMethod = CollectionMethod.Automatic
                    }
                }, ct);

            var s = subResp.Subscription;
            return new SubscriptionDto
            {
                Id = s?.Id ?? 0,
                State = s?.State?.Value ?? "",
                ProductName = s?.Product?.Name ?? "",
                ProductHandle = productHandle,
                PriceInDollars = (s?.ProductPriceInCents ?? 0) / 100.0m,
                NextBillingDate = s?.NextAssessmentAt,
                ActivatedAt = s?.ActivatedAt,
                CreatedAt = s?.CreatedAt ?? DateTimeOffset.UtcNow
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var errors = string.Join(", ", errorList.Errors ?? Array.Empty<string>());
                throw new InvalidOperationException($"Subscription creation rejected: {errors}", ex);
            }
            if (ex.Error.TryGetRawError(out var rawSub))
            {
                throw new InvalidOperationException(
                    $"Failed to create subscription: HTTP {(int)rawSub.StatusCode}", ex);
            }
            throw new InvalidOperationException("Failed to create subscription: unknown error", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create subscription: HTTP {(int)ex.Error.StatusCode}", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        string userReference, CancellationToken ct = default)
    {
        try
        {
            var custResp = await _client.Customers.ReadCustomerByReference(userReference, ct);
            var customerId = custResp.Customer!.Id!.Value;

            var subs = await _client.Customers.ListCustomerSubscriptions(customerId, ct);

            return subs.Select(sr => new SubscriptionDto
            {
                Id = sr.Subscription?.Id ?? 0,
                State = sr.Subscription?.State?.Value ?? "",
                ProductName = sr.Subscription?.Product?.Name ?? "",
                ProductHandle = sr.Subscription?.Product?.Handle ?? "",
                PriceInDollars = (sr.Subscription?.ProductPriceInCents ?? 0) / 100.0m,
                NextBillingDate = sr.Subscription?.NextAssessmentAt,
                ActivatedAt = sr.Subscription?.ActivatedAt,
                CreatedAt = sr.Subscription?.CreatedAt ?? DateTimeOffset.UtcNow
            }).ToList();
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Array.Empty<SubscriptionDto>();
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException(
                $"Failed to list subscriptions: HTTP {(int)ex.Error.StatusCode}", ex);
        }
    }

    private async Task<int> EnsureCustomerExistsAsync(
        string userReference, string email, string firstName, string lastName,
        CancellationToken ct)
    {
        try
        {
            var custResp = await _client.Customers.ReadCustomerByReference(userReference, ct);
            return custResp.Customer!.Id!.Value;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var newCust = await CreateCustomerAsync(userReference, email, firstName, lastName, ct);
            return newCust;
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException(
                $"Failed to look up customer: HTTP {(int)ex.Error.StatusCode}", ex);
        }
    }

    private async Task<int> CreateCustomerAsync(
        string userReference, string email, string firstName, string lastName,
        CancellationToken ct)
    {
        try
        {
            var custResp = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = userReference
                    }
                }, ct);
            return custResp.Customer!.Id!.Value;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResp))
            {
                throw new InvalidOperationException(
                    $"Customer creation rejected: {errorResp}", ex);
            }
            if (ex.Error.TryGetRawError(out var rawCust))
            {
                throw new InvalidOperationException(
                    $"Failed to create customer: HTTP {(int)rawCust.StatusCode}", ex);
            }
            throw new InvalidOperationException("Failed to create customer: unknown error", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create customer: HTTP {(int)ex.Error.StatusCode}", ex);
        }
    }
}
