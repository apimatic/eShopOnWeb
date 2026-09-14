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
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing SDK.
/// Maxio is the billing system of record; this adapter translates between the eShopOnWeb domain and
/// the SDK, enforces idempotent enrollment, and converts every provider/transport failure into a
/// single <see cref="SubscriptionBillingException"/> carrying a caller-safe message and HTTP status.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // SubscriptionState wire values that mean the subscription is NOT live, so re-subscribing is allowed.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioBillingService> _logger;

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
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        return await ExecuteAsync(async () =>
        {
            var plans = new List<SubscriptionPlan>();
            const int perPage = 100;
            int page = 1;

            while (true)
            {
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
                        page: page,
                        perPage: perPage,
                        ct: cancellationToken);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    throw TranslateListProducts(ex);
                }

                foreach (var productResponse in products)
                {
                    var product = productResponse.Product;
                    if (product is null || string.IsNullOrWhiteSpace(product.Handle))
                    {
                        continue;
                    }

                    plans.Add(new SubscriptionPlan
                    {
                        Handle = product.Handle,
                        Name = product.Name ?? string.Empty,
                        PriceInCents = product.PriceInCents ?? 0,
                        Currency = _settings.Currency,
                        Interval = product.Interval ?? 0,
                        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty
                    });
                }

                if (products.Count < perPage)
                {
                    break;
                }

                page++;
            }

            return (IReadOnlyList<SubscriptionPlan>)plans;
        }, "list subscription plans", cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        // Validate the requested plan against the configured family so unknown plans fail cleanly (400)
        // and we never subscribe a user to a product outside this catalog.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionBillingException(
                $"Subscription plan '{request.PlanHandle}' is not available.", statusCode: 400);
        }

        // Idempotency, part 1: ensure exactly one Maxio customer exists for this user (keyed on reference).
        var customerId = await EnsureCustomerAsync(request, cancellationToken);

        // Idempotency, part 2: reuse an existing live subscription to the same plan instead of duplicating.
        var existing = await ListSubscriptionsAsync(customerId, cancellationToken);
        var live = existing.FirstOrDefault(s =>
            string.Equals(s.PlanHandle, request.PlanHandle, StringComparison.Ordinal) && IsLive(s.State));
        if (live is not null)
        {
            _logger.LogInformation(
                "Reusing existing subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
                live.Id, customerId, request.PlanHandle);
            return new SubscribeResult { Subscription = live, AlreadyExisted = true };
        }

        var created = await CreateSubscriptionAsync(customerId, request.PlanHandle, plan, cancellationToken);
        _logger.LogInformation(
            "Created subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
            created.Id, customerId, request.PlanHandle);
        return new SubscribeResult { Subscription = created, AlreadyExisted = false };
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(
        string userReference, CancellationToken cancellationToken = default)
    {
        var customerId = await TryReadCustomerIdByReferenceAsync(userReference, cancellationToken);
        if (customerId is null)
        {
            // No billing customer yet => the user has never subscribed.
            return Array.Empty<CustomerSubscription>();
        }

        return await ListSubscriptionsAsync(customerId.Value, cancellationToken);
    }

    // --- customer resolution (idempotent) -------------------------------------------------------

    private async Task<int> EnsureCustomerAsync(SubscribeRequest request, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerIdByReferenceAsync(request.UserReference, cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        var (firstName, lastName) = DeriveCustomerName(request);

        return await ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.Customers.CreateCustomer(
                    new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = request.Email,
                            Reference = request.UserReference
                        }
                    },
                    ct: cancellationToken);

                return RequireCustomerId(response);
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                // A concurrent create (double-click) or a duplicate-reference rejection can race us here.
                // Reconcile by re-reading the customer by reference before treating this as a failure.
                var reconciled = await TryReadCustomerIdByReferenceAsync(request.UserReference, cancellationToken);
                if (reconciled is not null)
                {
                    return reconciled.Value;
                }

                throw TranslateCreateCustomer(ex);
            }
        }, "create the billing customer", cancellationToken);
    }

    private async Task<int?> TryReadCustomerIdByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        return await ExecuteAsync<int?>(async () =>
        {
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
                return response.Customer?.Id;
            }
            catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == (int)HttpStatusCode.NotFound)
            {
                // A genuine miss: no customer with this reference. (Distinct from an unreadable body,
                // which ExecuteAsync maps to a 5xx — never to an absence.)
                return null;
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRawError(ex, "look up the billing customer");
            }
        }, "look up the billing customer", cancellationToken);
    }

    // --- subscription reads / writes ------------------------------------------------------------

    private async Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId, string planHandle, SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            try
            {
                var response = await _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            CustomerId = customerId,
                            ProductHandle = planHandle,
                            PaymentCollectionMethod = ResolveCollectionMethod()
                        }
                    },
                    ct: cancellationToken);

                var subscription = response.Subscription
                    ?? throw new SubscriptionBillingException(
                        "The billing provider returned an empty subscription.", statusCode: 502);

                return MapSubscription(subscription, planHandle, plan);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                throw TranslateCreateSubscription(ex);
            }
        }, "create the subscription", cancellationToken);
    }

    private async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        int customerId, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            IReadOnlyList<SubscriptionResponse> responses;
            try
            {
                responses = await _client.Customers.ListCustomerSubscriptions(customerId, ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRawError(ex, "list the customer's subscriptions");
            }

            var subscriptions = new List<CustomerSubscription>(responses.Count);
            foreach (var response in responses)
            {
                var subscription = response.Subscription;
                if (subscription is null)
                {
                    continue;
                }

                var handle = subscription.Product?.Handle ?? string.Empty;
                subscriptions.Add(MapSubscription(subscription, handle, plan: null));
            }

            return (IReadOnlyList<CustomerSubscription>)subscriptions;
        }, "list the customer's subscriptions", cancellationToken);
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
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
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRawError(ex, "list product families");
            }

            var family = families
                .Select(f => f.ProductFamily)
                .FirstOrDefault(f => f is not null &&
                    string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal));

            if (family?.Id is null)
            {
                // Misconfiguration: the configured family handle does not exist on this site.
                throw new SubscriptionBillingException(
                    $"The configured Maxio product family '{_settings.ProductFamilyHandle}' was not found.",
                    statusCode: 500);
            }

            return family.Id.Value;
        }, "resolve the product family", cancellationToken);
    }

    // --- mapping --------------------------------------------------------------------------------

    private CustomerSubscription MapSubscription(Subscription subscription, string fallbackHandle, SubscriptionPlan? plan)
    {
        return new CustomerSubscription
        {
            Id = subscription.Id,
            PlanHandle = subscription.Product?.Handle ?? fallbackHandle,
            PlanName = subscription.Product?.Name ?? plan?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents
                ?? subscription.CurrentBillingAmountInCents
                ?? plan?.PriceInCents
                ?? 0,
            Currency = _settings.Currency,
            State = subscription.State?.Value ?? string.Empty,
            NextBillingDate = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };
    }

    private static bool IsLive(string state) =>
        !string.IsNullOrEmpty(state) && !TerminalStates.Contains(state);

    private CollectionMethod ResolveCollectionMethod() =>
        CollectionMethod.FromValue(_settings.PaymentCollectionMethod);

    private static (string FirstName, string LastName) DeriveCustomerName(SubscribeRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FirstName) && !string.IsNullOrWhiteSpace(request.LastName))
        {
            return (request.FirstName!, request.LastName!);
        }

        var email = request.Email ?? string.Empty;
        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email[..atIndex] : email;

        var firstName = string.IsNullOrWhiteSpace(request.FirstName)
            ? (string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart)
            : request.FirstName!;
        var lastName = string.IsNullOrWhiteSpace(request.LastName) ? "eShopOnWeb Customer" : request.LastName!;

        return (firstName, lastName);
    }

    private static int RequireCustomerId(CustomerResponse response) =>
        response.Customer?.Id
            ?? throw new SubscriptionBillingException(
                "The billing provider returned a customer without an id.", statusCode: 502);

    // --- failure translation --------------------------------------------------------------------

    /// <summary>
    /// Runs an SDK call, letting already-translated <see cref="SubscriptionBillingException"/>s through and
    /// converting transport failures and unreadable bodies into the same exception type. Operation-specific
    /// <c>SdkException</c> handling happens inside <paramref name="call"/>, where the concrete error type is known.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<Task<T>> call, string action, CancellationToken cancellationToken)
    {
        try
        {
            return await call();
        }
        catch (SubscriptionBillingException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches the model, or an error body that did not match its typed
            // error shape. Either way the outcome is unknown at this boundary => surface as a gateway error
            // (never as a domain absence). See the error-handling guidance for this hazard.
            _logger.LogWarning("Maxio returned an unprocessable response while trying to {Action}: {Error}", action, ex.Message);
            throw new SubscriptionBillingException(
                "The billing provider returned a response that could not be processed.", statusCode: 502, ex);
        }
        catch (Exception ex) when (
            ex is HttpRequestException ||
            (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning("The billing provider was unreachable while trying to {Action}: {Error}", action, ex.Message);
            throw new SubscriptionBillingException(
                $"The billing provider is currently unreachable while trying to {action}.", statusCode: 503, ex);
        }
    }

    private SubscriptionBillingException TranslateRawError(SdkException<RawError> ex, string action) =>
        TranslateRawError(ex.Error, action, ex);

    private SubscriptionBillingException TranslateRawError(RawError raw, string action, Exception? inner)
    {
        var status = MapStatus(raw.StatusCode);
        var detail = status is >= 400 and < 500 ? SafeBody(raw) : null;
        _logger.LogWarning("Maxio returned {Status} while trying to {Action}.", (int)raw.StatusCode, action);
        return new SubscriptionBillingException(
            detail is not null
                ? $"The billing provider rejected the request to {action}: {detail}"
                : $"The billing provider failed to {action}.",
            status, inner);
    }

    private SubscriptionBillingException TranslateCreateCustomer(SdkException<CreateCustomerError> ex)
    {
        // 422 validation lands in the typed slot first; its generated payload is unreliable (see the plan's
        // trap note), so surface a clear 422 without trusting its fields.
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new SubscriptionBillingException(
                "The billing provider rejected the customer details.", statusCode: 422, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw, "create the billing customer", ex);
        }

        return new SubscriptionBillingException(
            "The billing provider failed to create the billing customer.", statusCode: 502, ex);
    }

    private SubscriptionBillingException TranslateCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errors))
        {
            var detail = errors?.Errors is { Count: > 0 } messages
                ? string.Join("; ", messages)
                : "validation error";
            return new SubscriptionBillingException(
                $"The billing provider rejected the subscription: {detail}", statusCode: 422, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw, "create the subscription", ex);
        }

        return new SubscriptionBillingException(
            "The billing provider failed to create the subscription.", statusCode: 502, ex);
    }

    private SubscriptionBillingException TranslateListProducts(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            // A 404 for a bad family id is a server-side misconfiguration, not a caller error.
            return new SubscriptionBillingException(
                $"The billing provider could not list plans for the configured family: {message}", statusCode: 500, ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw, "list subscription plans", ex);
        }

        return new SubscriptionBillingException(
            "The billing provider failed to list subscription plans.", statusCode: 502, ex);
    }

    private static int MapStatus(HttpStatusCode code)
    {
        var value = (int)code;
        return value is >= 400 and < 500 ? value : 502;
    }

    private static string? SafeBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            body = body.Trim();
            return body.Length > 500 ? body[..500] : body;
        }
        catch
        {
            return null;
        }
    }
}
