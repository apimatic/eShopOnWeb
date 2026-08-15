using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing SDK.
/// All provider failures are translated into <see cref="MaxioBillingException"/> at this boundary so
/// callers see one failure type with an HTTP-mappable status. Enrollment is idempotent: it looks up
/// the customer/subscription before creating, and serializes concurrent subscribe calls per user.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;

    // In-process guard so a double-clicked subscribe for the same user runs one-at-a-time; combined
    // with the look-up-before-create logic this prevents duplicate customers/subscriptions.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeLocks = new();

    // Subscription states that are dead — a subscription in one of these is not reused on subscribe.
    private static readonly HashSet<string> _terminalStates =
        new(StringComparer.OrdinalIgnoreCase) { "canceled", "expired", "failed_to_create" };

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = RequireProductFamilyHandle();
        int familyId = await ResolveProductFamilyIdAsync(familyHandle, cancellationToken);

        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 200,
                ct: cancellationToken);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex) { throw TranslateListProducts(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }

        var plans = new List<SubscriptionPlan>();
        foreach (var pr in products)
        {
            var p = pr.Product;
            if (p is null)
            {
                continue;
            }

            plans.Add(new SubscriptionPlan
            {
                Handle = p.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Description = p.Description,
                PriceInCents = p.PriceInCents ?? 0,
                Interval = p.Interval ?? 0,
                IntervalUnit = p.IntervalUnit?.Value,
                PaymentMethodRequired = p.RequireCreditCard ?? false
            });
        }

        return plans;
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken = default)
    {
        var handle = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle : planHandle.Trim();
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new MaxioBillingException(
                "No plan handle was supplied and no default plan is configured.", HttpStatusCode.BadRequest);
        }

        var gate = _subscribeLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            int customerId = await EnsureCustomerAsync(subscriber, cancellationToken);

            var existing = await FindReusableSubscriptionAsync(customerId, handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Maxio: reusing existing subscription {0} for {1} on plan {2}.",
                    existing.SubscriptionId, subscriber.Reference, handle);
                return existing;
            }

            Subscription created;
            try
            {
                created = await CreateSubscriptionOnceAsync(customerId, handle, CollectionMethod.Remittance, cancellationToken);
            }
            catch (MaxioBillingException ex)
                when (ex.StatusCode == HttpStatusCode.UnprocessableEntity && IsCollectionMethodRejected(ex.Message))
            {
                // Sites on the legacy Statements architecture accept 'invoice' rather than 'remittance'
                // as the non-auto-charge collection method — retry once with that.
                _logger.LogWarning("Maxio rejected the 'remittance' collection method; retrying with 'invoice'.");
                created = await CreateSubscriptionOnceAsync(customerId, handle, CollectionMethod.Invoice, cancellationToken);
            }

            _logger.LogInformation(
                "Maxio: created subscription {0} for {1} on plan {2}.",
                created.Id ?? 0, subscriber.Reference, handle);
            return MapSubscription(created);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(
        SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        int? customerId = await FindCustomerIdAsync(subscriber.Reference, cancellationToken);
        if (!customerId.HasValue)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);
        var result = new List<CustomerSubscription>();
        foreach (var sr in subscriptions)
        {
            if (sr.Subscription is not null)
            {
                result.Add(MapSubscription(sr.Subscription));
            }
        }

        return result;
    }

    // ---- Provider building blocks -------------------------------------------------------------

    private async Task<int> ResolveProductFamilyIdAsync(string familyHandle, CancellationToken ct)
    {
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
        catch (SdkException<RawError> ex) { throw TranslateRaw(ex, "listing product families"); }
        catch (JsonException ex) { throw Unreadable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }

        foreach (var fr in families)
        {
            var family = fr.ProductFamily;
            if (family?.Handle == familyHandle && family.Id.HasValue)
            {
                return family.Id.Value;
            }
        }

        throw new MaxioBillingException(
            $"No Maxio product family with handle '{familyHandle}' exists on the configured site.",
            HttpStatusCode.NotFound);
    }

    private async Task<int> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken ct)
    {
        int? existing = await FindCustomerIdAsync(subscriber.Reference, ct);
        if (existing.HasValue)
        {
            return existing.Value;
        }

        try
        {
            var body = new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = subscriber.Reference
                }
            };
            var response = await _client.Customers.CreateCustomer(body, ct);
            int? id = response.Customer?.Id;
            if (id.HasValue)
            {
                _logger.LogInformation("Maxio: created customer {0} for reference {1}.", id.Value, subscriber.Reference);
                return id.Value;
            }

            // No id returned — reconcile by re-reading rather than assuming failure.
            int? reread = await FindCustomerIdAsync(subscriber.Reference, ct);
            if (reread.HasValue)
            {
                return reread.Value;
            }

            throw new MaxioBillingException(
                "The billing provider did not return an id for the created customer.", HttpStatusCode.BadGateway);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent create (duplicate reference) is the expected race here — reconcile first.
            int? reread = await FindCustomerIdAsync(subscriber.Reference, ct);
            if (reread.HasValue)
            {
                return reread.Value;
            }

            throw TranslateCreateCustomer(ex);
        }
        catch (JsonException ex) { throw Unreadable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
    }

    private async Task<int?> FindCustomerIdAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex) { throw TranslateRaw(ex, "looking up the customer"); }
        // NB: a JsonException here is "the answer was unreadable", NOT "no customer" — never map it
        // to absence, or a corrupt lookup would trigger a spurious customer create.
        catch (JsonException ex) { throw Unreadable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(customerId, ct);
        }
        catch (SdkException<RawError> ex) { throw TranslateRaw(ex, "listing subscriptions"); }
        catch (JsonException ex) { throw Unreadable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
    }

    private async Task<Subscription> CreateSubscriptionOnceAsync(
        int customerId, string productHandle, CollectionMethod collectionMethod, CancellationToken ct)
    {
        Subscription? created;
        try
        {
            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    // Invoice/remit the balance instead of auto-charging, so no payment method is
                    // required on file. No PaymentProfileId/CreditCardAttributes/etc. are set.
                    PaymentCollectionMethod = collectionMethod
                }
            };
            var response = await _client.Subscriptions.CreateSubscription(body, ct);
            created = response.Subscription;
        }
        catch (SdkException<CreateSubscriptionError> ex) { throw TranslateCreateSubscription(ex); }
        catch (JsonException ex) { throw Unreadable(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }

        if (created is null)
        {
            throw new MaxioBillingException(
                "The billing provider did not return the created subscription.", HttpStatusCode.BadGateway);
        }

        return created;
    }

    private static bool IsCollectionMethodRejected(string message) =>
        message.Contains("collection method", StringComparison.OrdinalIgnoreCase)
        || message.Contains("collection_method", StringComparison.OrdinalIgnoreCase);

    private async Task<CustomerSubscription?> FindReusableSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        foreach (var sr in subscriptions)
        {
            var s = sr.Subscription;
            if (s is null)
            {
                continue;
            }

            string? state = s.State?.Value;
            bool sameProduct = string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase);
            bool alive = state is null || !_terminalStates.Contains(state);
            if (sameProduct && alive)
            {
                return MapSubscription(s);
            }
        }

        return null;
    }

    private static CustomerSubscription MapSubscription(Subscription s) => new()
    {
        SubscriptionId = s.Id ?? 0,
        PlanHandle = s.Product?.Handle,
        PlanName = s.Product?.Name,
        PriceInCents = s.ProductPriceInCents ?? 0,
        State = s.State?.Value,
        CurrentPeriodStartedAt = s.CurrentPeriodStartedAt,
        NextBillingDate = s.CurrentPeriodEndsAt,
        CustomerReference = s.Customer?.Reference
    };

    // ---- Configuration + error translation ----------------------------------------------------

    private string RequireProductFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioBillingException(
                "Maxio product family handle is not configured (Maxio:ProductFamilyHandle).",
                HttpStatusCode.InternalServerError);
        }

        return _settings.ProductFamilyHandle!.Trim();
    }

    private static bool IsTransport(Exception ex) => ex is HttpRequestException or TaskCanceledException;

    private MaxioBillingException TranslateRaw(SdkException<RawError> ex, string action)
    {
        HttpStatusCode status = ex.Error.StatusCode;
        _logger.LogWarning("Maxio error while {0}: HTTP {1}.", action, (int)status);
        return new MaxioBillingException(
            $"The billing provider returned an error while {action} (HTTP {(int)status}).", status, ex);
    }

    private MaxioBillingException TranslateListProducts(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            return new MaxioBillingException($"The billing provider could not list products: {message}", HttpStatusCode.NotFound, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return new MaxioBillingException(
                $"The billing provider could not list products (HTTP {(int)raw.StatusCode}).", raw.StatusCode, ex);
        }

        return new MaxioBillingException("The billing provider could not list products.", HttpStatusCode.BadGateway, ex);
    }

    private MaxioBillingException TranslateCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
        {
            _logger.LogWarning("Maxio rejected customer create: {0}", FormatCustomerErrors(typed));
            return new MaxioBillingException($"The billing provider rejected the customer: {FormatCustomerErrors(typed)}", HttpStatusCode.UnprocessableEntity, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return new MaxioBillingException(
                $"The billing provider rejected the customer (HTTP {(int)raw.StatusCode}).", raw.StatusCode, ex);
        }

        return new MaxioBillingException("The billing provider rejected the customer.", HttpStatusCode.BadGateway, ex);
    }

    private MaxioBillingException TranslateCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var typed))
        {
            string detail = typed.Errors is { Count: > 0 } ? string.Join("; ", typed.Errors) : "validation failed";
            _logger.LogWarning("Maxio rejected subscription create: {0}", detail);
            return new MaxioBillingException($"The billing provider rejected the subscription: {detail}", HttpStatusCode.UnprocessableEntity, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return new MaxioBillingException(
                $"The billing provider rejected the subscription (HTTP {(int)raw.StatusCode}).", raw.StatusCode, ex);
        }

        return new MaxioBillingException("The billing provider rejected the subscription.", HttpStatusCode.BadGateway, ex);
    }

    private static string FormatCustomerErrors(CustomerErrorResponse1 error)
    {
        var messages = new List<string>();
        if (error.Errors?.PerPage is { Count: > 0 } perPage)
        {
            messages.AddRange(perPage);
        }

        if (error.Errors?.PricePoint is { Count: > 0 } pricePoint)
        {
            messages.AddRange(pricePoint);
        }

        return messages.Count > 0 ? string.Join("; ", messages) : "validation failed";
    }

    private MaxioBillingException Unreadable(JsonException ex)
    {
        _logger.LogWarning("Maxio returned a response that could not be parsed: {0}", ex.Message);
        return new MaxioBillingException(
            "The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
    }

    private MaxioBillingException Unreachable(Exception ex)
    {
        _logger.LogWarning("Maxio is unreachable: {0}", ex.Message);
        return new MaxioBillingException(
            "The billing provider is currently unreachable.", HttpStatusCode.ServiceUnavailable, ex);
    }
}
