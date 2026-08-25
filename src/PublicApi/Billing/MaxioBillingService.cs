using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Fronts the Maxio Advanced Billing SDK. All SDK failures are translated here into
/// <see cref="MaxioBillingException"/> with a caller-safe message and a deliberate status;
/// no SDK exception type escapes this boundary.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        var familyId = await GetProductFamilyIdAsync(ct);

        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await Bounded(token => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 50,
                ct: token), ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var message))
            {
                throw new MaxioBillingException(HttpStatusCode.NotFound,
                    $"The configured product family was not found: {message}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioBillingException(raw.StatusCode, "The billing provider rejected the plan listing request.", ex);
            }
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw TranslateUnexpected(ex, ct);
        }

        return products
            .Select(p => p.Product)
            .Where(p => p.ArchivedAt is null)
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit?.Value
            })
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string username, string productHandle, CancellationToken ct = default)
    {
        var customer = await FindOrCreateCustomerAsync(username, ct);

        // Deterministic reference: a double-click (or a retried request) with the same
        // plan resolves to the same subscription instead of creating a second one.
        var reference = $"{username}:{productHandle}";

        var existing = await TryFindSubscriptionAsync(reference, ct);
        if (existing is not null)
        {
            return Map(existing, productHandle);
        }

        try
        {
            var created = await Bounded(token => _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customer.Id,
                        Reference = reference,
                        // Invoice-based collection: the subscription activates and an invoice
                        // is issued without requiring a card on file (automatic capture would
                        // 422 with "no payment method on file" for cardless customers).
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                ct: token), ct);

            if (created.Subscription is null)
            {
                throw new MaxioBillingException(HttpStatusCode.BadGateway,
                    "The billing provider returned an empty subscription response.");
            }

            return Map(created.Subscription, productHandle);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                // 422 — possibly a lost race on the deterministic reference; settle by re-reading.
                var raced = await TryFindSubscriptionAsync(reference, ct);
                if (raced is not null)
                {
                    return Map(raced, productHandle);
                }

                throw new MaxioBillingException(HttpStatusCode.UnprocessableEntity,
                    $"The billing provider rejected the subscription: {string.Join("; ", errorList.Errors)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioBillingException(raw.StatusCode, "The billing provider rejected the subscription request.", ex);
            }
            throw;
        }
        catch (HttpRequestException ex)
        {
            // Transport failure on a write: the create may have reached Maxio. Reconcile
            // against provider state instead of assuming nothing happened.
            var settled = await TryFindSubscriptionAsync(reference, ct);
            if (settled is not null)
            {
                return Map(settled, productHandle);
            }

            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing provider could not be reached; the subscription outcome is unknown and was not created locally.", ex);
        }
        catch (Exception ex) when (ex is TaskCanceledException or JsonException)
        {
            throw TranslateUnexpected(ex, ct);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken ct = default)
    {
        var customer = await TryReadCustomerAsync(username, ct);
        if (customer?.Id is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await Bounded(token => _client.Customers.ListCustomerSubscriptions(
                customerId: customer.Id.Value,
                ct: token), ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioBillingException(ex.Error.StatusCode, "The billing provider rejected the subscription listing request.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw TranslateUnexpected(ex, ct);
        }

        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => Map(s!, null))
            .ToList();
    }

    private async Task<int> GetProductFamilyIdAsync(CancellationToken ct)
    {
        var cacheKey = $"maxio:product-family-id:{_settings.ProductFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out int cachedId))
        {
            return cachedId;
        }

        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioBillingException(HttpStatusCode.InternalServerError,
                "Maxio:ProductFamilyHandle is not configured.");
        }

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await Bounded(token => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: token), ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioBillingException(ex.Error.StatusCode, "The billing provider rejected the product family request.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw TranslateUnexpected(ex, ct);
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(pf => pf?.Handle == _settings.ProductFamilyHandle);

        if (match?.Id is null)
        {
            throw new MaxioBillingException(HttpStatusCode.NotFound,
                $"Product family '{_settings.ProductFamilyHandle}' was not found on the configured Maxio site.");
        }

        _cache.Set(cacheKey, match.Id.Value, TimeSpan.FromMinutes(10));
        return match.Id.Value;
    }

    private async Task<Customer> FindOrCreateCustomerAsync(string username, CancellationToken ct)
    {
        var existing = await TryReadCustomerAsync(username, ct);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveNames(username);
        var email = username.Contains('@') ? username : $"{username}@eshoponweb.local";

        try
        {
            var created = await Bounded(token => _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = username
                    }
                },
                ct: token), ct);

            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The server enforces one customer per reference, so a lost race surfaces here
            // as a 422. Whatever the status, settle by re-reading the reference first.
            var status = HttpStatusCode.UnprocessableEntity;
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                status = HttpStatusCode.UnprocessableEntity;
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                status = raw.StatusCode;
            }

            var raced = await TryReadCustomerAsync(username, ct);
            if (raced is not null)
            {
                return raced;
            }

            throw new MaxioBillingException(status, "The billing provider rejected the customer creation request.", ex);
        }
        catch (JsonException ex)
        {
            // The generated 422 payload shape for this operation may not match a real
            // duplicate-reference body; a JsonException here is the rejection with its
            // status lost. Settle by re-reading the reference.
            var raced = await TryReadCustomerAsync(username, ct);
            if (raced is not null)
            {
                return raced;
            }

            throw new MaxioBillingException(HttpStatusCode.UnprocessableEntity,
                "The billing provider rejected the customer creation request.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw TranslateUnexpected(ex, ct);
        }
    }

    private async Task<Customer?> TryReadCustomerAsync(string username, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(token => _client.Customers.ReadCustomerByReference(
                reference: username,
                ct: token), ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioBillingException(ex.Error.StatusCode, "The billing provider rejected the customer lookup request.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw TranslateUnexpected(ex, ct);
        }
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(token => _client.Subscriptions.FindSubscription(
                reference: reference,
                ct: token), ct);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioBillingException(raw.StatusCode, "The billing provider rejected the subscription lookup request.", ex);
            }
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw TranslateUnexpected(ex, ct);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private MaxioBillingException TranslateUnexpected(Exception ex, CancellationToken ct)
    {
        return ex switch
        {
            // A drifted/malformed 2xx body: outcome unknown.
            JsonException => new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex),
            TaskCanceledException when !ct.IsCancellationRequested => new MaxioBillingException(HttpStatusCode.GatewayTimeout,
                "The billing provider did not respond in time.", ex),
            _ => new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing provider could not be reached.", ex)
        };
    }

    private static SubscriptionDto Map(Subscription subscription, string? fallbackProductHandle)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State?.Value,
            ProductHandle = subscription.Product?.Handle ?? fallbackProductHandle,
            ProductName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents,
            // The read model carries no next_billing_at; next_assessment_at is the
            // next-billing signal, with the period end as fallback.
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }

    private static (string FirstName, string LastName) DeriveNames(string username)
    {
        var local = username.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? Capitalize(parts[0]) : "Customer";
        var lastName = parts.Length > 1 ? Capitalize(parts[^1]) : "eShopOnWeb";
        return (firstName, lastName);
    }

    private static string Capitalize(string value)
    {
        return string.IsNullOrEmpty(value)
            ? value
            : string.Concat(value[..1].ToUpperInvariant(), value[1..]);
    }
}
