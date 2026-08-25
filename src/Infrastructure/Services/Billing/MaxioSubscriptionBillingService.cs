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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Billing;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing.
/// Idempotency: the Maxio customer is keyed on <c>Customer.Reference</c> = eShopOnWeb user id
/// (find-or-create), and each subscription carries a deterministic
/// <c>{userId}:{productHandle}</c> reference used both to short-circuit duplicates and to
/// reconcile writes whose outcome is unknown after a transport failure.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // States in which an existing subscription means "already subscribed" (non-terminal).
    private static readonly HashSet<string> LiveStates = new(StringComparer.Ordinal)
    {
        "active", "trialing", "past_due", "on_hold", "awaiting_signup",
        "soft_failure", "unpaid", "suspended", "paused", "pending", "assessing"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly SemaphoreSlim _familyIdLock = new(1, 1);
    private int? _productFamilyId;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await GetProductFamilyIdAsync(cancellationToken);

        var products = await Guarded(
            c => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                ct: c),
            nameof(ListPlansAsync), cancellationToken);

        return products
            .Where(p => p.Product.ArchivedAt is null)
            .Select(p => new SubscriptionPlanDto
            {
                ProductId = p.Product.Id,
                Handle = p.Product.Handle,
                Name = p.Product.Name,
                PriceInCents = p.Product.PriceInCents,
                Interval = p.Product.Interval,
                IntervalUnit = p.Product.IntervalUnit?.Value
            })
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        string userId, string email, string? firstName, string? lastName,
        string planHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(p => p.Handle == planHandle))
        {
            throw new BillingServiceException((int)HttpStatusCode.NotFound,
                $"No subscription plan '{planHandle}' is available.");
        }

        var customerId = await EnsureCustomerAsync(userId, email, firstName, lastName, cancellationToken);

        var existing = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        var duplicate = existing.FirstOrDefault(s =>
            s.Product?.Handle == planHandle && s.State is not null && LiveStates.Contains(s.State.Value));
        if (duplicate is not null)
        {
            return Map(duplicate);
        }

        var reference = $"{userId}:{planHandle}";
        try
        {
            var created = await CreateSubscriptionAsync(customerId, planHandle, reference, cancellationToken);
            return Map(created.Subscription);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Unknown outcome: the write may have reached Maxio (transport retries resend on any
            // verb). Reconcile by reference before deciding it is safe to send the create again.
            _logger.LogWarning(ex, "CreateSubscription outcome unknown; reconciling by reference.");
            var settled = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (settled is not null)
            {
                return Map(settled.Subscription);
            }

            var retried = await CreateSubscriptionAsync(customerId, planHandle, reference, cancellationToken);
            return Map(retried.Subscription);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        Customer customer;
        try
        {
            var response = await Bounded(c => _client.Customers.ReadCustomerByReference(userId, ct: c), cancellationToken);
            customer = response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<SubscriptionDto>();
        }
        catch (Exception ex) when (ex is not BillingServiceException)
        {
            throw Convert(ex, nameof(ListMySubscriptionsAsync));
        }

        if (customer.Id is null)
        {
            throw new BillingServiceException((int)HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.");
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<int> EnsureCustomerAsync(
        string userId, string email, string? firstName, string? lastName, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await Bounded(c => _client.Customers.ReadCustomerByReference(userId, ct: c), cancellationToken);
            return RequireCustomerId(existing.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // No customer yet — fall through to create.
        }
        catch (Exception ex) when (ex is not BillingServiceException)
        {
            throw Convert(ex, nameof(EnsureCustomerAsync));
        }

        var (first, last) = DeriveName(email, firstName, lastName);
        try
        {
            var created = await Bounded(c => _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = first,
                        LastName = last,
                        Email = email,
                        Reference = userId
                    }
                }, ct: c), cancellationToken);
            return RequireCustomerId(created.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // 422 with an unmodeled error body: possibly a duplicate-reference race against a
                // concurrent create. Re-read by reference; only if that misses is it a real rejection.
                try
                {
                    var reread = await Bounded(c => _client.Customers.ReadCustomerByReference(userId, ct: c), cancellationToken);
                    return RequireCustomerId(reread.Customer);
                }
                catch (SdkException<RawError> readEx) when (readEx.Error.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new BillingServiceException((int)HttpStatusCode.UnprocessableEntity,
                        "The billing provider rejected the customer record.", ex);
                }
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ConvertRaw(raw, nameof(EnsureCustomerAsync), ex);
            }

            throw new BillingServiceException((int)HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is not BillingServiceException)
        {
            throw Convert(ex, nameof(EnsureCustomerAsync));
        }
    }

    private async Task<SubscriptionResponse> CreateSubscriptionAsync(
        int customerId, string planHandle, string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(c => _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        CustomerId = customerId,
                        ProductHandle = planHandle,
                        Reference = reference,
                        PaymentCollectionMethod = CollectionMethod.FromValue(
                            _settings.PaymentCollectionMethod ?? "remittance")
                    }
                }, ct: c), cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                throw new BillingServiceException((int)HttpStatusCode.UnprocessableEntity,
                    $"The billing provider rejected the subscription: {string.Join("; ", errors.Errors)}", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ConvertRaw(raw, nameof(CreateSubscriptionAsync), ex);
            }

            throw new BillingServiceException((int)HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex);
        }
    }

    private async Task<SubscriptionResponse?> FindSubscriptionByReferenceAsync(
        string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(c => _client.Subscriptions.FindSubscription(reference, ct: c), cancellationToken);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ConvertRaw(raw, nameof(FindSubscriptionByReferenceAsync), ex);
            }

            throw new BillingServiceException((int)HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex);
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken)
    {
        var responses = await Guarded(
            c => _client.Customers.ListCustomerSubscriptions(customerId, ct: c),
            nameof(ListCustomerSubscriptionsAsync), cancellationToken);

        return responses
            .Where(r => r.Subscription is not null)
            .Select(r => r.Subscription!)
            .ToList();
    }

    private async Task<string> GetProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_productFamilyId is not null)
        {
            return _productFamilyId.Value.ToString();
        }

        await _familyIdLock.WaitAsync(cancellationToken);
        try
        {
            if (_productFamilyId is null)
            {
                var families = await Guarded(
                    c => _client.ProductFamilies.ListProductFamilies(null, null, null, null, null, ct: c),
                    nameof(GetProductFamilyIdAsync), cancellationToken);

                var match = families.FirstOrDefault(f => f.ProductFamily?.Handle == _settings.ProductFamilyHandle);
                if (match?.ProductFamily?.Id is null)
                {
                    throw new BillingServiceException((int)HttpStatusCode.InternalServerError,
                        "The configured billing product family was not found.");
                }

                _productFamilyId = match.ProductFamily.Id;
            }

            return _productFamilyId.Value.ToString();
        }
        finally
        {
            _familyIdLock.Release();
        }
    }

    private async Task<T> Guarded<T>(Func<CancellationToken, Task<T>> call, string operation, CancellationToken ct)
    {
        try
        {
            return await Bounded(call, ct);
        }
        catch (Exception ex) when (ex is not BillingServiceException)
        {
            throw Convert(ex, operation);
        }
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private BillingServiceException Convert(Exception ex, string operation) => ex switch
    {
        SdkException<RawError> sdk => ConvertRaw(sdk.Error, operation, sdk),
        JsonException json => new BillingServiceException((int)HttpStatusCode.BadGateway,
            "The billing provider returned a response that could not be processed.", json),
        HttpRequestException or TaskCanceledException => new BillingServiceException(
            (int)HttpStatusCode.ServiceUnavailable, "The billing provider could not be reached.", ex),
        _ => new BillingServiceException((int)HttpStatusCode.BadGateway,
            "The billing provider returned a response that could not be processed.", ex)
    };

    private BillingServiceException ConvertRaw(RawError raw, string operation, Exception inner)
    {
        var body = raw.ReadAsString();
        _logger.LogWarning("Maxio {Operation} failed: HTTP {Status} {Body}", operation, (int)raw.StatusCode, body);

        return raw.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new BillingServiceException(
                (int)HttpStatusCode.BadGateway, "The billing provider rejected the service credentials.", inner),
            HttpStatusCode.NotFound => new BillingServiceException(
                (int)HttpStatusCode.NotFound, "The requested billing record was not found.", inner),
            HttpStatusCode.UnprocessableEntity => new BillingServiceException(
                (int)HttpStatusCode.UnprocessableEntity, $"The billing provider rejected the request: {body}", inner),
            _ => new BillingServiceException(
                (int)HttpStatusCode.BadGateway, "The billing provider could not complete the request.", inner)
        };
    }

    private static int RequireCustomerId(Customer customer) => customer.Id
        ?? throw new BillingServiceException((int)HttpStatusCode.BadGateway,
            "The billing provider returned a response that could not be processed.");

    private static SubscriptionDto Map(Subscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State?.Value,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit?.Value,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
    };

    private static (string First, string Last) DeriveName(string email, string? firstName, string? lastName)
    {
        if (!string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName))
        {
            return (firstName, lastName);
        }

        var local = email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? parts[0] : local;
        var last = parts.Length > 1 ? parts[^1] : "Customer";
        return (first, last);
    }
}
