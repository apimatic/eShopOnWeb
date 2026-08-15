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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing .NET SDK.
/// Maxio is the system of record; nothing about subscriptions is persisted locally.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Subscription states that count as "the user already has this plan" for idempotency purposes.
    private static readonly HashSet<string> ActiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        SubscriptionState.Active.Value,
        SubscriptionState.Trialing.Value,
        SubscriptionState.Assessing.Value,
        SubscriptionState.Pending.Value,
        SubscriptionState.SoftFailure.Value,
        SubscriptionState.PastDue.Value,
        SubscriptionState.OnHold.Value,
    };

    // Serializes subscribe calls per user (reference) within this process so a double-click cannot
    // race two find-or-create / subscribe sequences. Combined with the pre-check below and Maxio's
    // uniqueness on the customer reference, this makes subscribe idempotent.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private const int PageSize = 100;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await ListFamilyProductsAsync(cancellationToken);
        return products
            .Select(pr => pr.Product)
            .Where(p => p is not null)
            .Select(MapPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerAsync(subscriber.Reference, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions
            .Select(sr => sr.Subscription)
            .Where(s => s is not null)
            .Select(MapSubscription)
            .OrderByDescending(s => s.NextBillingAt)
            .ToList();
    }

    public async Task<SubscriptionResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        // Validate the requested plan against the configured product family up front, so an unknown
        // handle is a clean 404 rather than an opaque 422 from CreateSubscription.
        var plans = await GetPlansAsync(cancellationToken);
        if (plans.All(p => !string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PlanNotFoundException(planHandle);
        }

        var gate = SubscribeLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);
            var customerId = customer.Id
                ?? throw new SubscriptionBillingException("Maxio returned a customer without an id.");

            // Idempotency pre-check: if the user already has an active subscription to this plan,
            // return it instead of creating a second one.
            var existing = await FindActiveSubscriptionAsync(customerId, planHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation($"User '{subscriber.Reference}' is already subscribed to plan '{planHandle}' (subscription {existing.Id}); returning existing.");
                return new SubscriptionResult(existing, AlreadyExisted: true);
            }

            var created = await CreateSubscriptionAsync(customerId, planHandle, subscriber.Reference, cancellationToken);
            return new SubscriptionResult(created, AlreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    // ----- Customer find-or-create -------------------------------------------------------------

    private async Task<Customer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(subscriber.Reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference,
            }
        };

        try
        {
            CustomerResponse response;
            using (MaxioWriteGuard.BeginSingleAttempt())
            {
                response = await _client.Customers.CreateCustomer(request, ct);
            }
            var customer = response.Customer
                ?? throw new SubscriptionBillingException("Maxio returned an empty customer after create.");
            _logger.LogInformation($"Created Maxio customer {customer.Id} for reference '{subscriber.Reference}'.");
            return customer;
        }
        catch (MaxioWriteResentException)
        {
            // The create may already have taken effect; reconcile by re-reading.
            var reconciled = await FindCustomerAsync(subscriber.Reference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw new SubscriptionBillingException("The customer could not be created reliably with the billing provider; please retry.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here most likely means the reference was taken between our lookup and create
            // (a concurrent request). Reconcile by re-reading; if now present, use it.
            var reconciled = await FindCustomerAsync(subscriber.Reference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw TranslateCreateCustomerError(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            // A transport failure on a write may have reached Maxio and been retried; reconcile.
            var reconciled = await FindCustomerAsync(subscriber.Reference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw new SubscriptionBillingException("The billing provider could not be reached while creating the customer.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException("The billing provider returned a customer response that could not be processed.", null, ex);
        }
    }

    /// <summary>Exact idempotent lookup by reference. Returns null only on a genuine 404 (absent).</summary>
    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // genuinely absent -> caller may create
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError("read the customer", ex.Error);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw new SubscriptionBillingException("The billing provider could not be reached while looking up the customer.", null, ex);
        }
        catch (JsonException ex)
        {
            // A drifted 2xx body is NOT a domain absence - do not treat as "not found".
            throw new SubscriptionBillingException("The billing provider returned a customer response that could not be processed.", null, ex);
        }
    }

    // ----- Subscriptions -----------------------------------------------------------------------

    private async Task<CustomerSubscription?> FindActiveSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        var match = subscriptions
            .Select(sr => sr.Subscription)
            .FirstOrDefault(s => s is not null
                && string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                && s.State is not null
                && ActiveStates.Contains(s.State.Value));

        return match is null ? null : MapSubscription(match);
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(customerId, ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError("list the customer's subscriptions", ex.Error);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw new SubscriptionBillingException("The billing provider could not be reached while listing subscriptions.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException("The billing provider returned a subscriptions response that could not be processed.", null, ex);
        }
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken ct)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                // Invoice collection => no stored card / no 3-DS required at creation.
                PaymentCollectionMethod = CollectionMethod.Invoice,
            }
        };

        try
        {
            SubscriptionResponse response;
            using (MaxioWriteGuard.BeginSingleAttempt())
            {
                response = await _client.Subscriptions.CreateSubscription(request, ct);
            }
            var subscription = response.Subscription
                ?? throw new SubscriptionBillingException("Maxio returned an empty subscription after create.");
            _logger.LogInformation($"Created Maxio subscription {subscription.Id} for customer {customerId} on plan '{planHandle}'.");
            return MapSubscription(subscription);
        }
        catch (MaxioWriteResentException)
        {
            // The write may already have taken effect; reconcile against provider state.
            var reconciled = await FindActiveSubscriptionAsync(customerId, planHandle, ct);
            if (reconciled is not null)
            {
                _logger.LogInformation($"Reconciled subscription {reconciled.Id} for customer {customerId} after a refused transport re-send.");
                return reconciled;
            }

            throw new SubscriptionBillingException("The subscription could not be confirmed with the billing provider; please retry.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw TranslateCreateSubscriptionError(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            // First-attempt transport failure that never reached the server (no re-send occurred).
            // Reconcile defensively before reporting failure.
            var reconciled = await FindActiveSubscriptionAsync(customerId, planHandle, ct);
            if (reconciled is not null)
            {
                _logger.LogInformation($"Reconciled subscription {reconciled.Id} for customer {customerId} after a transport failure.");
                return reconciled;
            }

            throw new SubscriptionBillingException("The billing provider could not be reached while creating the subscription.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException("The billing provider returned a subscription response that could not be processed.", null, ex);
        }
    }

    // ----- Products (plans) --------------------------------------------------------------------

    private async Task<IReadOnlyList<ProductResponse>> ListFamilyProductsAsync(CancellationToken ct)
    {
        var familyId = $"handle:{_settings.ProductFamilyHandle}";
        var all = new List<ProductResponse>();
        var page = 1;

        try
        {
            while (true)
            {
                var pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PageSize,
                    ct: ct);

                if (pageItems is null || pageItems.Count == 0)
                {
                    break;
                }

                all.AddRange(pageItems);

                if (pageItems.Count < PageSize)
                {
                    break;
                }

                page++;
            }

            return all;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                throw new SubscriptionBillingException(
                    $"The configured Maxio product family '{_settings.ProductFamilyHandle}' was not found: {notFound}",
                    HttpStatusCode.NotFound, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError("list the plans", raw);
            }

            throw new SubscriptionBillingException("The billing provider rejected the request to list plans.", null, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            throw new SubscriptionBillingException("The billing provider could not be reached while listing plans.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException("The billing provider returned a plans response that could not be processed.", null, ex);
        }
    }

    // ----- Mapping -----------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        ProductId = product.Id ?? 0,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
    };

    private static CustomerSubscription MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        State = subscription.State?.Value ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        NextBillingAt = subscription.CurrentPeriodEndsAt,
    };

    // ----- Error translation -------------------------------------------------------------------

    private static bool IsTransportFailure(Exception ex)
        => ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException;

    private static SubscriptionBillingException TranslateRawError(string action, RawError error)
    {
        var status = error.StatusCode;
        var detail = SafeReadBody(error);
        var message = status == HttpStatusCode.Unauthorized
            ? "The billing provider rejected the credentials."
            : $"The billing provider failed to {action}.";
        return new SubscriptionBillingException(
            string.IsNullOrWhiteSpace(detail) ? message : $"{message} {detail}",
            status);
    }

    private static SubscriptionBillingException TranslateCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        // The typed 422 model (CustomerErrorResponse1) carries only per_page/price_point arrays and
        // no human message, so fall back to the raw body for a readable detail.
        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError("create the customer", raw);
        }

        return new SubscriptionBillingException(
            "The billing provider rejected the customer details.", HttpStatusCode.UnprocessableEntity, ex);
    }

    private static SubscriptionBillingException TranslateCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errors) && errors?.Errors is { Count: > 0 } messages)
        {
            return new SubscriptionBillingException(
                $"The billing provider rejected the subscription: {string.Join("; ", messages)}",
                HttpStatusCode.UnprocessableEntity, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError("create the subscription", raw);
        }

        return new SubscriptionBillingException(
            "The billing provider rejected the subscription.", HttpStatusCode.UnprocessableEntity, ex);
    }

    private static string SafeReadBody(RawError error)
    {
        try
        {
            var body = error.ReadAsString();
            return string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
