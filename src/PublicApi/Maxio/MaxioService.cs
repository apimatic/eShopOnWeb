using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;

    public MaxioService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _productFamilyHandle = options.Value.ProductFamilyHandle;
    }

    public async Task<IReadOnlyList<PlanDto>> GetPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct);

            var family = families.FirstOrDefault(f => f.ProductFamily?.Handle == _productFamilyHandle);
            if (family == null)
            {
                throw new InvalidOperationException($"Product family '{_productFamilyHandle}' not found.");
            }

            var familyId = family.ProductFamily!.Id!.Value.ToString();

            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId,
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
                .Select(p => p.Product)
                .Where(p => p != null && p.ArchivedAt == null)
                .Select(p => new PlanDto
                {
                    Id = p!.Id ?? 0,
                    Name = p.Name ?? "",
                    Handle = p.Handle ?? "",
                    Description = p.Description ?? "",
                    PriceInCents = p.PriceInCents ?? 0,
                    PriceInDollars = (p.PriceInCents ?? 0) / 100m,
                    Interval = p.Interval ?? 1,
                    IntervalUnit = p.IntervalUnit?.Value ?? "month",
                    RequireCreditCard = p.RequireCreditCard ?? false
                })
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new InvalidOperationException($"Failed to list plans (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", ex);
            }
            throw new InvalidOperationException($"Failed to list plans: {ex.Message}", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to list plans (HTTP {(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("Maxio API is unreachable. Please try again later.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Maxio API returned an unexpected response. Details: {ex.Message}", ex);
        }
    }

    public async Task<SubscriptionResult> SubscribeAsync(string userId, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default)
    {
        var customer = await EnsureCustomerExistsAsync(userId, email, firstName, lastName, ct);

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customer.Id,
                ProductHandle = productHandle,
                CustomerReference = userId,
                PaymentCollectionMethod = CollectionMethod.Invoice,
                DeferSignup = true
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct: ct);
            var sub = response.Subscription;
            return new SubscriptionResult
            {
                SubscriptionId = sub?.Id ?? 0,
                State = sub?.State?.Value ?? "unknown",
                ProductName = sub?.Product?.Name ?? "",
                ProductHandle = sub?.Product?.Handle ?? "",
                PriceInCents = sub?.ProductPriceInCents ?? 0,
                PriceInDollars = (sub?.ProductPriceInCents ?? 0) / 100m,
                NextBillingAt = sub?.NextAssessmentAt,
                ActivatedAt = sub?.ActivatedAt,
                CreatedAt = sub?.CreatedAt
            };
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var errorStrings = errorList.Errors?.ToList() ?? new List<string>();
                throw new InvalidOperationException($"Subscription creation failed: {string.Join("; ", errorStrings)}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new InvalidOperationException($"Subscription creation failed (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}");
            }
            throw new InvalidOperationException($"Subscription creation failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("Maxio API is unreachable. Please try again later.", ex);
        }
    }

    public async Task<IReadOnlyList<MySubscriptionDto>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        var customer = await FindCustomerByReferenceAsync(userId, ct);
        if (customer == null)
        {
            return Array.Empty<MySubscriptionDto>();
        }

        try
        {
            var customerId = customer.Id!.Value;
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
            return subscriptions
                .Where(s => s.Subscription != null)
                .Select(s => new MySubscriptionDto
                {
                    SubscriptionId = s.Subscription!.Id ?? 0,
                    State = s.Subscription.State?.Value ?? "unknown",
                    ProductName = s.Subscription.Product?.Name ?? "",
                    ProductHandle = s.Subscription.Product?.Handle ?? "",
                    PriceInCents = s.Subscription.ProductPriceInCents ?? 0,
                    PriceInDollars = (s.Subscription.ProductPriceInCents ?? 0) / 100m,
                    NextBillingAt = s.Subscription.NextAssessmentAt,
                    ActivatedAt = s.Subscription.ActivatedAt,
                    CreatedAt = s.Subscription.CreatedAt,
                    CurrentPeriodEndsAt = s.Subscription.CurrentPeriodEndsAt
                })
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw new InvalidOperationException($"Failed to list subscriptions (HTTP {(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("Maxio API is unreachable. Please try again later.", ex);
        }
    }

    private async Task<Customer> EnsureCustomerExistsAsync(string userId, string email, string firstName, string lastName, CancellationToken ct)
    {
        var existing = await FindCustomerByReferenceAsync(userId, ct);
        if (existing != null)
        {
            return existing;
        }

        var createBody = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userId
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(createBody, ct: ct);
            return response.Customer!;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse))
            {
                throw new InvalidOperationException($"Customer creation failed: {errorResponse.Errors?.ToString() ?? ex.Message}");
            }
            if (ex.Error.TryGetRawError(out var raw) && raw.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var retry = await FindCustomerByReferenceAsync(userId, ct);
                if (retry != null) return retry;
            }
            throw new InvalidOperationException($"Customer creation failed: {ex.Message}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("Maxio API is unreachable. Please try again later.", ex);
        }
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("Maxio API is unreachable. Please try again later.", ex);
        }
    }
}

public class PlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public string Description { get; set; } = "";
    public long PriceInCents { get; set; }
    public decimal PriceInDollars { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "";
    public bool RequireCreditCard { get; set; }
}

public class SubscriptionResult
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public long PriceInCents { get; set; }
    public decimal PriceInDollars { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class MySubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public long PriceInCents { get; set; }
    public decimal PriceInDollars { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
