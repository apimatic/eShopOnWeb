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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="IMaxioSubscriptionService"/>.
/// Maxio is the billing system of record; this service never persists billing state
/// locally — every read/write goes to Maxio, which keeps the integration idempotent
/// across process restarts (important given the in-memory identity store).
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Subscription states that mean "no longer a live enrollment" — a shopper in one of
    // these may re-subscribe to the same plan.
    private static readonly HashSet<string> InactiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private const string DefaultCurrency = "USD";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var familyHandle = RequireFamilyHandle();
        var familyId = await ResolveProductFamilyIdAsync(familyHandle, cancellationToken);
        var products = await ListProductsAsync(familyId, cancellationToken);

        return products
            .Select(p => p.Product)
            .Where(p => p != null)
            .Select(p => MapPlan(p!, familyHandle))
            .OrderBy(p => p.Price)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(command);

        var familyHandle = RequireFamilyHandle();

        // Validate the requested plan against the live catalog (and resolve a default when
        // none was supplied) so we return a clean 404 rather than a Maxio 422.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = ResolveTargetPlan(command.PlanHandle, plans);

        // 1) Idempotently ensure a Maxio customer exists for this shopper.
        var customer = await EnsureCustomerAsync(command.Subscriber, cancellationToken);
        var customerId = customer.Id
            ?? throw new MaxioIntegrationException("Maxio returned a customer without an id.", 502);

        // 2) Idempotency guard: if the shopper already holds a live subscription to this
        //    plan, return it instead of creating a duplicate.
        var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle, cancellationToken);
        if (existing != null)
        {
            _logger.LogInformation(
                $"Shopper {command.Subscriber.UserId} already has live subscription {existing.Id} to plan '{plan.Handle}'; returning existing.");
            return new SubscribeResult(existing, AlreadyExisted: true);
        }

        // 3) Create the subscription — no payment method captured.
        var created = await CreateSubscriptionAsync(customerId, plan.Handle, cancellationToken);
        _logger.LogInformation(
            $"Created Maxio subscription {created.Id} for shopper {command.Subscriber.UserId} on plan '{plan.Handle}'.");
        return new SubscribeResult(created, AlreadyExisted: false);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ArgumentNullException.ThrowIfNull(subscriber);

        // Read-only: never create a customer here.
        var customer = await TryReadCustomerAsync(subscriber.UserId, cancellationToken);
        if (customer?.Id == null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s != null)
            .Select(s => MapSubscription(s!))
            .OrderByDescending(s => s.CurrentPeriodStartsAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    // ---------------------------------------------------------------------------------
    // Customer (idempotent find-or-create)
    // ---------------------------------------------------------------------------------

    private async Task<Customer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken ct)
    {
        var reference = subscriber.UserId;

        var existing = await TryReadCustomerAsync(reference, ct);
        if (existing != null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveName(subscriber);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = reference
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct: ct);
            var customer = response.Customer
                ?? throw new MaxioIntegrationException("Maxio returned an empty customer on create.", 502);
            return customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent request (double-click) may have created the customer between our
            // read and this create — the reference is unique, so re-read and reuse it.
            var raced = await TryReadCustomerAsync(reference, ct);
            if (raced != null)
            {
                return raced;
            }

            string detail;
            if (ex.Error.TryGetCustomerErrorResponse1(out var validation)
                && (validation.Errors?.PerPage?.Any() == true || validation.Errors?.PricePoint?.Any() == true))
            {
                var messages = (validation.Errors?.PerPage ?? Enumerable.Empty<string>())
                    .Concat(validation.Errors?.PricePoint ?? Enumerable.Empty<string>());
                detail = string.Join("; ", messages);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                detail = SafeReadRaw(raw);
            }
            else
            {
                detail = "unknown validation error";
            }

            _logger.LogWarning($"Maxio rejected customer creation for '{reference}': {detail}");
            throw new MaxioIntegrationException($"Maxio could not create the customer: {detail}", 422);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    private async Task<Customer?> TryReadCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Genuine miss — the caller decides whether to create.
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex, "look up the Maxio customer");
        }
        catch (JsonException ex)
        {
            // An unreadable body is NOT a "not found" — surface it, never silently create.
            throw UnprocessableResponse(ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    // ---------------------------------------------------------------------------------
    // Product family / products (plans)
    // ---------------------------------------------------------------------------------

    private async Task<string> ResolveProductFamilyIdAsync(string familyHandle, CancellationToken ct)
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
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex, "list Maxio product families");
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f != null
                && string.Equals(f.Handle, familyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id == null)
        {
            throw new MaxioIntegrationException(
                $"No Maxio product family found with handle '{familyHandle}'.", 404);
        }

        return match.Id.Value.ToString();
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(string familyId, CancellationToken ct)
    {
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId,
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
                ct: ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                throw new MaxioIntegrationException(
                    $"Maxio product family '{familyId}' was not found: {notFound}", 404);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, "list Maxio products");
            }

            throw new MaxioIntegrationException("Maxio failed to list products.", 502);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    // ---------------------------------------------------------------------------------
    // Subscriptions
    // ---------------------------------------------------------------------------------

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        var live = subscriptions
            .Select(s => s.Subscription)
            .Where(s => s != null)
            .FirstOrDefault(s => IsLive(s!) && MatchesPlan(s!, planHandle));

        return live == null ? null : MapSubscription(live);
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex, "list the shopper's Maxio subscriptions");
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    private async Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                // Remittance (invoice) collection so the first period is invoiced rather than
                // charged automatically — this is what lets a subscribe succeed with no card /
                // no 3-DS on file. No payment-profile fields are set for the same reason.
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct: ct);
            var subscription = response.Subscription
                ?? throw new MaxioIntegrationException("Maxio returned an empty subscription on create.", 502);
            return MapSubscription(subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            string detail;
            if (ex.Error.TryGetErrorListResponse1(out var validation) && validation.Errors?.Any() == true)
            {
                detail = string.Join("; ", validation.Errors);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                detail = SafeReadRaw(raw);
            }
            else
            {
                detail = "unknown validation error";
            }

            _logger.LogWarning($"Maxio rejected subscription creation on plan '{productHandle}': {detail}");
            throw new MaxioIntegrationException($"Maxio could not create the subscription: {detail}", 422);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex);
        }
    }

    // ---------------------------------------------------------------------------------
    // Mapping
    // ---------------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product p, string familyHandle) => new(
        Id: p.Id ?? 0,
        Handle: p.Handle ?? string.Empty,
        Name: p.Name ?? p.Handle ?? "Unnamed plan",
        Description: p.Description,
        Price: CentsToAmount(p.PriceInCents),
        Currency: DefaultCurrency,
        Interval: FormatInterval(p.Interval, p.IntervalUnit?.Value),
        ProductFamilyHandle: familyHandle);

    private static CustomerSubscription MapSubscription(Subscription s)
    {
        var product = s.Product;
        return new CustomerSubscription(
            Id: s.Id ?? 0,
            State: StateOf(s),
            PlanName: product?.Name ?? product?.Handle ?? "Subscription",
            PlanHandle: product?.Handle,
            Price: CentsToAmount(product?.PriceInCents ?? s.CurrentBillingAmountInCents),
            Currency: DefaultCurrency,
            Interval: FormatInterval(product?.Interval, product?.IntervalUnit?.Value),
            CurrentPeriodStartsAt: s.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt: s.CurrentPeriodEndsAt,
            // The Subscription payload carries no next_billing_at; current_period_ends_at is
            // the effective next billing date.
            NextBillingDate: s.CurrentPeriodEndsAt,
            CustomerId: s.Customer?.Id ?? 0,
            CustomerReference: s.Customer?.Reference);
    }

    private static string StateOf(Subscription s) => s.State?.Value ?? "unknown";

    private static bool IsLive(Subscription s) => !InactiveStates.Contains(StateOf(s));

    private static bool MatchesPlan(Subscription s, string planHandle) =>
        string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase);

    private static decimal CentsToAmount(long? cents) => cents.HasValue ? cents.Value / 100m : 0m;

    private static string FormatInterval(int? interval, string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return string.Empty;
        }

        var normalizedUnit = unit.ToLowerInvariant();
        return interval is > 1
            ? $"every {interval} {normalizedUnit}s"
            : normalizedUnit;
    }

    private SubscriptionPlan ResolveTargetPlan(string? requestedHandle, IReadOnlyList<SubscriptionPlan> plans)
    {
        if (plans.Count == 0)
        {
            throw new MaxioIntegrationException(
                "No subscription plans are available in the configured product family.", 422);
        }

        if (!string.IsNullOrWhiteSpace(requestedHandle))
        {
            var match = plans.FirstOrDefault(p =>
                string.Equals(p.Handle, requestedHandle, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                throw new MaxioIntegrationException(
                    $"Subscription plan '{requestedHandle}' was not found.", 404);
            }

            return match;
        }

        // No plan specified — default to the least expensive plan on offer.
        return plans[0];
    }

    private static (string FirstName, string LastName) ResolveName(SubscriberIdentity subscriber)
    {
        var first = subscriber.FirstName;
        if (string.IsNullOrWhiteSpace(first))
        {
            var localPart = subscriber.Email.Split('@').FirstOrDefault();
            first = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        }

        var last = string.IsNullOrWhiteSpace(subscriber.LastName) ? "Subscriber" : subscriber.LastName!;
        return (first!, last);
    }

    // ---------------------------------------------------------------------------------
    // Configuration & error translation
    // ---------------------------------------------------------------------------------

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new MaxioIntegrationException(
                "Maxio billing is not configured. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl).",
                503);
        }
    }

    private string RequireFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioIntegrationException(
                "Maxio:ProductFamilyHandle is not configured.", 503);
        }

        return _settings.ProductFamilyHandle!;
    }

    private MaxioIntegrationException TranslateRaw(SdkException<RawError> ex, string action) =>
        TranslateRawError(ex.Error, action);

    private MaxioIntegrationException TranslateRawError(RawError raw, string action)
    {
        var status = (int)raw.StatusCode;
        var body = SafeReadRaw(raw);
        _logger.LogWarning($"Maxio error while trying to {action}: HTTP {status} {body}");

        // A provider 4xx is actionable by the caller and is preserved; anything else has no
        // meaningful client status and surfaces as 502.
        var clientStatus = status is >= 400 and < 500 ? status : 502;
        return new MaxioIntegrationException($"Maxio failed to {action} (HTTP {status}).", clientStatus);
    }

    private static string SafeReadRaw(RawError raw)
    {
        try
        {
            // RawError bodies are frequently not JSON — ReadAsString is the safe accessor.
            return raw.ReadAsString() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private MaxioIntegrationException UnprocessableResponse(JsonException ex)
    {
        _logger.LogWarning($"Maxio returned a response that could not be processed: {ex.Message}");
        return new MaxioIntegrationException(
            "Maxio returned a response that could not be processed.", ex, 502);
    }

    private MaxioIntegrationException Unreachable(Exception ex)
    {
        _logger.LogWarning($"Maxio Advanced Billing was unreachable: {ex.Message}");
        return new MaxioIntegrationException("Maxio Advanced Billing is currently unreachable.", ex, 503);
    }
}
