using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// ISubscriptionService backed by Maxio Advanced Billing. Every outbound call is bounded
/// by a whole-call cancellation budget; SDK failures are translated to BillingException
/// with caller-safe messages at this one boundary.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            var familyId = await ResolveProductFamilyIdAsync(ct);

            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    ct: ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFoundMessage))
                {
                    throw new BillingException($"Billing plan catalog was not found: {notFoundMessage}", (int)HttpStatusCode.NotFound, ex);
                }
                else if (ex.Error.TryGetRawError(out var raw))
                {
                    throw MapRawError(raw, ex);
                }
                throw new BillingException("The billing provider rejected the plan listing request.", null, ex);
            }

            return (IReadOnlyList<SubscriptionPlanDto>)products
                .Where(p => p.Product is not null)
                .Select(p => new SubscriptionPlanDto
                {
                    Handle = p.Product.Handle ?? string.Empty,
                    Name = p.Product.Name ?? string.Empty,
                    PriceInCents = p.Product.PriceInCents,
                    Interval = p.Product.Interval,
                    IntervalUnit = p.Product.IntervalUnit?.Value
                })
                .ToList();
        }, cancellationToken);
    }

    public async Task<SubscriptionDto> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            var customerId = await EnsureCustomerAsync(command, ct);

            // Deterministic reference makes a retried/double-submitted subscribe harmless.
            var reference = $"{command.UserId}:{command.ProductHandle}";

            var existing = await FindSubscriptionOrNullAsync(reference, ct);
            if (existing is not null)
            {
                return Map(existing);
            }

            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = command.ProductHandle,
                    Reference = reference
                }
            };

            try
            {
                var created = await _client.Subscriptions.CreateSubscription(body: request, ct: ct);
                if (created.Subscription is null)
                {
                    throw new BillingException("The billing provider returned an empty subscription.", null, null);
                }
                return Map(created.Subscription);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    var message = errorList.Errors is { Count: > 0 }
                        ? string.Join("; ", errorList.Errors)
                        : "The billing provider rejected the subscription request.";
                    throw new BillingException(message, (int)HttpStatusCode.UnprocessableEntity, ex);
                }
                else if (ex.Error.TryGetRawError(out var raw))
                {
                    throw MapRawError(raw, ex);
                }
                throw new BillingException("The billing provider rejected the subscription request.", null, ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // A transport failure on a write has an unknown outcome: the request may have
                // reached Maxio (and may even have been retried by the SDK pipeline). Settle it
                // by re-reading provider state before reporting failure.
                var settled = await FindSubscriptionOrNullAsync(reference, CancellationToken.None);
                if (settled is not null)
                {
                    return Map(settled);
                }
                throw new BillingException("The billing provider could not be reached and the subscription outcome is unknown; retry the request.", null, ex);
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            int customerId;
            try
            {
                var customer = await _client.Customers.ReadCustomerByReference(userId, ct: ct);
                customerId = customer.Customer.Id ?? throw new BillingException("The billing provider returned a customer without an id.");
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return (IReadOnlyList<SubscriptionDto>)Array.Empty<SubscriptionDto>();
            }
            catch (SdkException<RawError> ex)
            {
                throw MapRawError(ex.Error, ex);
            }

            try
            {
                var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
                return (IReadOnlyList<SubscriptionDto>)subscriptions
                    .Where(s => s.Subscription is not null)
                    .Select(s => Map(s.Subscription!))
                    .ToList();
            }
            catch (SdkException<RawError> ex)
            {
                throw MapRawError(ex.Error, ex);
            }
        }, cancellationToken);
    }

    private async Task<int> EnsureCustomerAsync(SubscribeCommand command, CancellationToken ct)
    {
        try
        {
            var existing = await _client.Customers.ReadCustomerByReference(command.UserId, ct: ct);
            return existing.Customer.Id ?? throw new BillingException("The billing provider returned a customer without an id.");
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Customer absent — create below.
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, ex);
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = command.FirstName,
                LastName = command.LastName,
                Email = command.Email,
                Reference = command.UserId
            }
        };

        try
        {
            var created = await _client.Customers.CreateCustomer(body: request, ct: ct);
            return created.Customer.Id ?? throw new BillingException("The billing provider returned a customer without an id.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // 422 — possibly a duplicate-reference race with a concurrent subscribe.
                // The generated 422 model cannot be trusted to carry the real reason, so
                // settle by re-reading: if the customer now exists, the race was won by us.
                try
                {
                    var reread = await _client.Customers.ReadCustomerByReference(command.UserId, ct: ct);
                    return reread.Customer.Id ?? throw new BillingException("The billing provider returned a customer without an id.");
                }
                catch (SdkException<RawError>)
                {
                    throw new BillingException("The billing provider rejected the customer request.", (int)HttpStatusCode.UnprocessableEntity, ex);
                }
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }
            throw new BillingException("The billing provider rejected the customer request.", null, ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionOrNullAsync(string reference, CancellationToken ct)
    {
        try
        {
            var found = await _client.Subscriptions.FindSubscription(reference: reference, ct: ct);
            return found.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null; // 404 — not yet created
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, ex);
            }
            throw new BillingException("The billing provider rejected the subscription lookup.", null, ex);
        }
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        var cacheKey = $"maxio:product-family-id:{_settings.ProductFamilyHandle}";
        if (_cache.TryGetValue(cacheKey, out int cachedId))
        {
            return cachedId;
        }

        // Numeric ids are reassigned when the sandbox is re-seeded, so the family id is
        // resolved from the stable configured handle at runtime and never hard-coded.
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, ex);
        }

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is not int familyId)
        {
            throw new BillingException($"The configured billing product family '{_settings.ProductFamilyHandle}' was not found.", (int)HttpStatusCode.NotFound);
        }

        _cache.Set(cacheKey, familyId, TimeSpan.FromHours(1));
        return familyId;
    }

    private static SubscriptionDto Map(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State?.Value,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            // The API does not return next_billing_at; current_period_ends_at is the next billing date.
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
        };
    }

    private static BillingException MapRawError(RawError raw, Exception inner)
    {
        // The raw body is deliberately not surfaced on the wire (it may contain provider internals).
        return new BillingException("The billing provider returned an error.", (int)raw.StatusCode, inner);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            // A 2xx whose body drifted from the model, or an error body that did not match its
            // generated shape — either way the provider's response could not be processed.
            throw new BillingException("The billing provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingException("The billing provider could not be reached.", null, ex);
        }
    }
}
