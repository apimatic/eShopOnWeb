using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing .NET SDK.
/// The SDK client is a long-lived singleton (see <see cref="MaxioBillingServiceCollectionExtensions"/>);
/// this service is the single seam that maps eShopOnWeb concepts onto SDK calls and translates every
/// provider/transport failure into <see cref="BillingException"/> so callers face one failure type.
/// </summary>
public sealed class MaxioBillingService : ISubscriptionBillingService
{
    // A whole-call deadline. RetryOptions.Timeout is per-attempt, so only a CancellationToken bounds
    // the entire operation (including retries). Every SDK call goes through Bounded(...).
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(30);

    // Terminal subscription states: a subscription in one of these no longer counts as an active
    // enrollment, so a new subscribe to the same plan is allowed rather than deduped.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        // The family-scoped products endpoint is keyed by the numeric family id, not the handle, so
        // resolve the configured handle to its id first (handles are stable; ids are reassigned on re-seed).
        int familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        try
        {
            var products = await Bounded(ct => _client.ProductFamilies.ListProductsForProductFamily(
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
                perPage: 200,
                ct: ct), cancellationToken);

            return products
                .Select(p => p.Product)
                .Where(p => !string.IsNullOrEmpty(p.Handle) && p.ArchivedAt is null)
                .Select(MapPlan)
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw FromListProductsError(ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Transport(ex, "loading subscription plans");
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(BillingUserIdentity user, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException("A plan handle is required to subscribe.", isCallerError: true, providerStatusCode: 400);
        }

        _logger.LogInformation("Subscribe requested for reference {Reference} to plan {PlanHandle}", user.Reference, planHandle);

        int customerId = await EnsureCustomerAsync(user, cancellationToken);

        // Idempotency guard: if the customer already has a non-terminal subscription to this plan,
        // return it instead of creating a second — so a double-submit never produces a duplicate.
        var existing = await ListCustomerSubscriptionResponsesAsync(customerId, cancellationToken);
        var duplicate = existing
            .Select(r => r.Subscription)
            .FirstOrDefault(s => s is not null
                && string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                && IsActiveState(s.State?.Value));
        if (duplicate is not null)
        {
            _logger.LogInformation("Subscribe deduped to existing subscription {SubscriptionId} for customer {CustomerId}", duplicate.Id, customerId);
            return new SubscribeResult { Subscription = MapSubscription(duplicate), AlreadyExisted = true };
        }

        var created = await CreateSubscriptionAsync(planHandle, customerId, cancellationToken);
        _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}", created.Id, customerId, planHandle);
        return new SubscribeResult { Subscription = MapSubscription(created), AlreadyExisted = false };
    }

    public async Task<IReadOnlyList<CustomerSubscriptionInfo>> GetSubscriptionsForUserAsync(BillingUserIdentity user, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerAsync(user.Reference, cancellationToken);
        if (customer?.Id is not int customerId)
        {
            return Array.Empty<CustomerSubscriptionInfo>();
        }

        var responses = await ListCustomerSubscriptionResponsesAsync(customerId, cancellationToken);
        return responses
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    /// <summary>
    /// Resolves the configured product-family handle to its numeric id via the site's family list.
    /// </summary>
    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await Bounded(ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "loading subscription plans");
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Transport(ex, "loading subscription plans");
        }

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null
                && string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));
        if (match?.Id is int id)
        {
            return id;
        }

        throw new BillingException("The configured subscription product family was not found on the billing site.", isCallerError: false, providerStatusCode: 404);
    }

    // --- Customer helpers -------------------------------------------------------------------

    /// <summary>Reads the customer by external reference; returns null when none exists (404).</summary>
    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Customers.ReadCustomerByReference(reference, ct: ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "looking up your billing account");
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Transport(ex, "looking up your billing account");
        }
    }

    /// <summary>
    /// Ensures a Maxio customer exists for the user and returns its id. Idempotent: the customer is
    /// keyed by the unique <c>reference</c>, so a create that loses a race surfaces as a 422 and is
    /// recovered by re-reading the customer that the winning request created.
    /// </summary>
    private async Task<int> EnsureCustomerAsync(BillingUserIdentity user, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(user.Reference, cancellationToken);
        if (existing?.Id is int id)
        {
            return id;
        }

        try
        {
            var created = await Bounded(ct => _client.Customers.CreateCustomer(BuildCreateCustomer(user), ct: ct), cancellationToken);
            if (created.Customer.Id is int newId)
            {
                _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}", newId, user.Reference);
                return newId;
            }

            throw new BillingException("The billing provider did not return a customer id.", isCallerError: false);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here most likely means a concurrent request already created the customer for this
            // reference — re-read and use it. Only if it is genuinely absent do we surface the error.
            var raced = await FindCustomerAsync(user.Reference, cancellationToken);
            if (raced?.Id is int racedId)
            {
                return racedId;
            }

            throw FromCreateCustomerError(ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Transport(ex, "creating your billing account");
        }
    }

    private static CreateCustomerRequest BuildCreateCustomer(BillingUserIdentity user)
    {
        var (first, last) = SplitName(user.Email, user.Reference);
        return new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = first,
                LastName = last,
                Email = user.Email,
                Reference = user.Reference
            }
        };
    }

    // --- Subscription helpers ---------------------------------------------------------------

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionResponsesAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "reading your subscriptions");
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Transport(ex, "reading your subscriptions");
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(string planHandle, int customerId, CancellationToken cancellationToken)
    {
        try
        {
            // Payment collection method is left unset (Maxio default): the seeded plans require no
            // payment method, so enrollment succeeds without capturing a card / 3-DS.
            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId
                }
            };

            var response = await Bounded(ct => _client.Subscriptions.CreateSubscription(body, ct: ct), cancellationToken);
            if (response.Subscription is { } subscription)
            {
                return subscription;
            }

            throw new BillingException("The billing provider did not return the created subscription.", isCallerError: false);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            throw FromCreateSubscriptionError(ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Transport(ex, "creating your subscription");
        }
    }

    private bool IsActiveState(string? state) => state is null || !TerminalStates.Contains(state);

    // --- Mapping ----------------------------------------------------------------------------

    private static SubscriptionPlanInfo MapPlan(Product product) => new()
    {
        Handle = product.Handle!,
        ProductId = product.Id,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value
    };

    private static CustomerSubscriptionInfo MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        State = subscription.State?.Value,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.CurrentBillingAmountInCents,
        Currency = subscription.Currency,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };

    private static (string First, string Last) SplitName(string email, string reference)
    {
        string local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        if (string.IsNullOrWhiteSpace(local))
        {
            local = string.IsNullOrWhiteSpace(reference) ? "eShop" : reference;
        }

        return (local, "eShopOnWeb Shopper");
    }

    // --- Bounding & error translation -------------------------------------------------------

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestBudget);
        return await call(cts.Token);
    }

    // A transport/timeout failure that is NOT the caller cancelling their own request. When the
    // caller's token is cancelled we let the exception propagate untranslated.
    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken)
        => ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested;

    private static BillingException Transport(Exception ex, string action)
        => new($"The billing provider is currently unavailable while {action}. Please try again shortly.", isCallerError: false, innerException: ex);

    private static BillingException UnprocessableResponse(Exception ex)
        => new("The billing provider returned a response that could not be processed.", isCallerError: false, innerException: ex);

    private static BillingException FromRaw(RawError raw, string action)
    {
        int status = (int)raw.StatusCode;
        // 401/403 = our credentials, 429 = our quota — not the caller's fault, so 5xx. Other 4xx are
        // caller-actionable and pass the status through; everything else is provider unavailability.
        bool callerError = status is >= 400 and < 500 && status is not 401 and not 403 and not 429;
        string message = callerError
            ? $"The billing provider rejected the request while {action} (HTTP {status})."
            : $"The billing provider is currently unavailable while {action}. Please try again shortly.";
        return new BillingException(message, callerError, status);
    }

    private static BillingException FromListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        // 404 here means the configured product family handle does not exist on the site — a
        // deployment/config fault, not something the caller can fix.
        if (ex.Error.TryGetString(out _))
        {
            return new BillingException("The configured subscription product family was not found on the billing site.", isCallerError: false, providerStatusCode: 404);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw, "loading subscription plans");
        }

        return new BillingException("The billing provider returned an unrecognized error while loading plans.", isCallerError: false);
    }

    private static BillingException FromCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var body))
        {
            string? detail = null;
            if (body.Errors is { } errors && errors.TryGetListOfString(out var messages))
            {
                detail = string.Join("; ", messages);
            }

            // The customer details are derived from the authenticated user, not caller-supplied, so a
            // validation failure is treated as a server-side fault (with the provider detail surfaced).
            string message = string.IsNullOrEmpty(detail)
                ? "Your billing account could not be created."
                : $"Your billing account could not be created: {detail}";
            return new BillingException(message, isCallerError: false, providerStatusCode: 422);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw, "creating your billing account");
        }

        return new BillingException("The billing provider returned an unrecognized error while creating your account.", isCallerError: false);
    }

    private static BillingException FromCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var body))
        {
            string detail = string.Join("; ", body.Errors);
            // The plan handle is caller-supplied, so a 422 here is caller-actionable (e.g. unknown plan).
            string message = string.IsNullOrEmpty(detail)
                ? "The subscription could not be created."
                : $"The subscription could not be created: {detail}";
            return new BillingException(message, isCallerError: true, providerStatusCode: 422);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw, "creating your subscription");
        }

        return new BillingException("The billing provider returned an unrecognized error while creating your subscription.", isCallerError: false);
    }
}
