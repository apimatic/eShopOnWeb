using System;
using System.Collections.Concurrent;
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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Maxio Advanced Billing implementation of the subscription billing service.
/// All SDK facts (signatures, wire names, error accessors) per maxio-plan.md.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        EnsureConfigured();
        try
        {
            var products = await Bounded(c => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: "handle:" + _settings.ProductFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 200,
                ct: c), ct);

            return products
                .Where(p => p.Product != null)
                .Select(p => MapPlan(p.Product!))
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFoundMessage))
            {
                throw new BillingException($"Billing plan catalog was not found: {notFoundMessage}", StatusCodes.Status404NotFound);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw);
            }
            throw new BillingException("The billing provider rejected the plan listing request.");
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(string username, string email, string productHandle, CancellationToken ct = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException("A product handle is required.", StatusCodes.Status400BadRequest);
        }

        // Serialize the double-click: two concurrent requests for the same user must not
        // both pass the not-found checks below.
        var userLock = UserLocks.GetOrAdd(username, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(ct);
        try
        {
            var customerId = await FindOrCreateCustomerAsync(username, email, ct);

            var reference = $"{username}:{productHandle}";
            var existing = await FindSubscriptionByReferenceAsync(reference, ct);
            if (existing != null)
            {
                return MapSubscription(existing);
            }

            try
            {
                var created = await Bounded(c => _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            CustomerId = customerId,
                            ProductHandle = productHandle,
                            Reference = reference
                        }
                    }, c), ct);

                if (created.Subscription == null)
                {
                    throw new BillingException("The billing provider returned an empty subscription.");
                }
                return MapSubscription(created.Subscription);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    var detail = string.Join("; ", errorList.Errors);
                    _logger.LogWarning("Maxio rejected subscription create for {Username}: {Detail}", username, detail);
                    throw new BillingException($"The billing provider rejected the subscription: {detail}", StatusCodes.Status422UnprocessableEntity, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw TranslateRaw(raw);
                }
                throw new BillingException("The billing provider rejected the subscription request.");
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                // Outcome unknown — the write may have reached Maxio. Reconcile before failing.
                var reconciled = await FindSubscriptionByReferenceAsync(reference, ct);
                if (reconciled != null)
                {
                    return MapSubscription(reconciled);
                }
                throw Unreachable(ex);
            }
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken ct = default)
    {
        EnsureConfigured();
        int customerId;
        try
        {
            var customer = await Bounded(c => _client.Customers.ReadCustomerByReference(username, c), ct);
            if (customer.Customer?.Id is not int id)
            {
                return Array.Empty<SubscriptionDto>();
            }
            customerId = id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<SubscriptionDto>();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }

        try
        {
            var subscriptions = await Bounded(c => _client.Customers.ListCustomerSubscriptions(customerId, c), ct);
            return subscriptions
                .Where(s => s.Subscription != null)
                .Select(s => MapSubscription(s.Subscription!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    private async Task<int> FindOrCreateCustomerAsync(string username, string email, CancellationToken ct)
    {
        try
        {
            var existing = await Bounded(c => _client.Customers.ReadCustomerByReference(username, c), ct);
            if (existing.Customer?.Id is int existingId)
            {
                return existingId;
            }
            throw new BillingException("The billing provider returned a customer without an id.");
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Not found — fall through to create.
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }

        var (firstName, lastName) = DeriveNames(username);
        try
        {
            var created = await Bounded(c => _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = username
                    }
                }, c), ct);

            if (created.Customer?.Id is int newId)
            {
                return newId;
            }
            throw new BillingException("The billing provider returned a customer without an id.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // Possibly a duplicate-reference race (another request created it first):
            // re-read by reference before giving up.
            if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
            {
                _logger.LogWarning("Maxio rejected customer create for {Username}; attempting re-read.", username);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogWarning("Maxio rejected customer create for {Username}: HTTP {Status}; attempting re-read.",
                    username, (int)raw.StatusCode);
            }

            try
            {
                var reread = await Bounded(c => _client.Customers.ReadCustomerByReference(username, c), ct);
                if (reread.Customer?.Id is int raceId)
                {
                    return raceId;
                }
            }
            catch (Exception readEx) when (readEx is SdkException<RawError> || IsTransportFailure(readEx) || readEx is JsonException)
            {
                _logger.LogError(readEx, "Re-read of Maxio customer {Username} failed after a rejected create.", username);
            }
            throw new BillingException("The billing provider rejected the customer enrollment.", StatusCodes.Status422UnprocessableEntity, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            // Outcome unknown — the customer may exist now. Reconcile by reference.
            try
            {
                var reread = await Bounded(c => _client.Customers.ReadCustomerByReference(username, c), ct);
                if (reread.Customer?.Id is int reconciledId)
                {
                    return reconciledId;
                }
            }
            catch (Exception readEx) when (readEx is SdkException<RawError> || IsTransportFailure(readEx) || readEx is JsonException)
            {
                _logger.LogError(readEx, "Re-read of Maxio customer {Username} failed after a transport failure on create.", username);
            }
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var found = await Bounded(c => _client.Subscriptions.FindSubscription(reference, c), ct);
            return found.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw);
            }
            throw new BillingException("The billing provider rejected the subscription lookup.");
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new BillingException(
                "Maxio billing is not configured. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl) via environment or user-secrets.",
                StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static (string FirstName, string LastName) DeriveNames(string username)
    {
        var local = username.Split('@')[0];
        var parts = local.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("Customer", "User"),
            1 => (parts[0], "User"),
            _ => (parts[0], string.Join(' ', parts.Skip(1)))
        };
    }

    private static SubscriptionPlanDto MapPlan(Product product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Handle = product.Handle,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value
    };

    private static SubscriptionDto MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State?.Value,
        PlanName = subscription.Product?.Name,
        PlanHandle = subscription.Product?.Handle,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit?.Value,
        NextBillingDate = subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };

    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

    private static BillingException Unreachable(Exception ex) =>
        new("The billing provider could not be reached.", StatusCodes.Status503ServiceUnavailable, ex);

    private static BillingException Unprocessable(JsonException ex) =>
        new("The billing provider returned a response that could not be processed.", StatusCodes.Status502BadGateway, ex);

    private static BillingException TranslateRaw(RawError raw)
    {
        var status = (int)raw.StatusCode;
        var message = status is >= 400 and < 500
            ? $"The billing provider rejected the request (HTTP {status})."
            : "The billing provider returned an error.";
        return new BillingException(message, status);
    }
}
