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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Talks to Maxio Advanced Billing (via the APIMatic-generated SDK) for the subscription
/// capability. This is the single boundary where Maxio/SDK failures become
/// <see cref="MaxioException"/> subtypes and where every outbound call gets a whole-call
/// budget (see <see cref="MaxioOptions.RequestTimeoutSeconds"/>).
///
/// Subscription creation is serialized per (customer, plan) with <see cref="SubscriptionGate"/>
/// and reconciled against provider state on any rejection, which makes repeated or concurrent
/// subscribe attempts idempotent: at most one active subscription is ever created for a
/// (customer, plan) pair. Customer creation is idempotent through Maxio's enforced uniqueness
/// on <c>reference</c> (a 422/conflict triggers a re-lookup instead of an error).
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // States after which a NEW subscription for the same plan is legitimate. Anything else
    // (active, trialing, past_due, unpaid, on_hold, suspended, ...) still counts as
    // "already subscribed" and is returned instead of creating a duplicate.
    private static readonly HashSet<string> RetryableStates = new(StringComparer.Ordinal)
    {
        SubscriptionState.Canceled.Value,
        SubscriptionState.Expired.Value,
        SubscriptionState.FailedToCreate.Value,
        SubscriptionState.TrialEnded.Value,
    };

    private static readonly SubscriptionGate Gate = new();

    private readonly MaxioAdvancedBillingClient? _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient? client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var budget = CreateBudget(cancellationToken);
        var ct = budget.Token;

        try
        {
            var products = await _client!.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: "handle:" + _options.ProductFamilyHandle,
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 100,
                ct: ct);

            var plans = new List<SubscriptionPlan>();
            foreach (var wrapper in products)
            {
                var product = wrapper.Product;
                if (product is null || product.ArchivedAt is not null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    string.IsNullOrWhiteSpace(product.Name) ? product.Handle : product.Name!,
                    product.PriceInCents ?? 0,
                    product.Interval,
                    product.IntervalUnit?.Value ?? "month",
                    product.RequireCreditCard ?? false));
            }

            _logger.LogInformation("Listed {Count} subscription plans from family {FamilyHandle}", plans.Count, _options.ProductFamilyHandle);
            return plans;
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw TranslateListPlansError(ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "The Maxio request failed.");
        }
        catch (HttpRequestException ex)
        {
            throw MaxioUnreachable(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw MaxioTimedOut(ex);
        }
        catch (JsonException ex)
        {
            throw MaxioUnreadable(ex);
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var planHandle = request.PlanHandle.Trim();
        if (planHandle.Length == 0)
        {
            throw new MaxioApiException(400, "A plan handle is required to subscribe.");
        }

        // Serialize per (customer, plan) so a double-click can never create two subscriptions.
        // The gate wait gets its own generous budget: queuing behind another subscribe is not
        // Maxio work, so it must not consume the per-call budget that bounds the Maxio calls.
        var gateKey = $"{request.Customer.CustomerReference}|{planHandle}";

        try
        {
            using (var gateWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                gateWait.CancelAfter(TimeSpan.FromSeconds(90));

                // The lease must be disposed when the held region exits (including returns and
                // exceptions) or the per-(customer, plan) semaphore is never released and every
                // later subscribe for the pair queues until the gate wait times out.
                using (await Gate.EnterAsync(gateKey, gateWait.Token))
                {
                    // Budget starts once the gate is held: it bounds the actual Maxio calls below.
                    using var budget = CreateBudget(cancellationToken);
                    var ct = budget.Token;

                    var customer = await GetOrCreateCustomerAsync(request.Customer, ct);
                    var customerReference = customer.Reference ?? request.Customer.CustomerReference;
                    var customerId = customer.Id
                        ?? throw new MaxioApiException(502, "Maxio returned a customer without an identifier.");

                    var existing = await FindActiveSubscriptionAsync(customerId, planHandle, ct);
                    if (existing is not null)
                    {
                        _logger.LogInformation("Subscription for plan {PlanHandle} already exists (state {State}); returning it", planHandle, existing.State);
                        return new SubscribeResult(existing, IsNew: false);
                    }

                    try
                    {
                        var created = await _client!.Subscriptions.CreateSubscription(
                            body: new CreateSubscriptionRequest
                            {
                                Subscription = new CreateSubscription
                                {
                                    ProductHandle = planHandle,
                                    CustomerReference = customerReference,
                                    // No card is captured in this flow. Without a future next_billing_at,
                                    // the whole first period is due at signup and Maxio rejects the create
                                    // when there is no payment method ("No payment method was on file for
                                    // ..."). Deferring the first billing onto the next period means no
                                    // payment is captured at creation; the first charge is attempted near
                                    // that date.
                                    NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1)
                                }
                            },
                            ct: ct);

                        var subscription = created.Subscription
                            ?? throw new MaxioApiException(502, "Maxio returned an empty subscription response.");

                        _logger.LogInformation("Created subscription {SubscriptionId} for plan {PlanHandle} (state {State})", subscription.Id, planHandle, subscription.State?.Value);
                        return new SubscribeResult(ToRecord(subscription), IsNew: true);
                    }
                    catch (SdkException<CreateSubscriptionError> ex)
                    {
                        // Provider rejection (422 duplicate/validation, or another non-success):
                        // reconcile with provider state before surfacing anything.
                        var afterReconcile = await FindActiveSubscriptionAsync(customerId, planHandle, ct);
                        if (afterReconcile is not null)
                        {
                            return new SubscribeResult(afterReconcile, IsNew: false);
                        }

                        throw TranslateCreateSubscriptionError(ex);
                    }
                    catch (JsonException ex)
                    {
                        // Unreadable body on the create (success or rejection). Outcome unknown:
                        // reconcile before surfacing; never report a definite failure on a maybe-write.
                        var afterReconcile = await FindActiveSubscriptionAsync(customerId, planHandle, ct);
                        if (afterReconcile is not null)
                        {
                            return new SubscribeResult(afterReconcile, IsNew: false);
                        }

                        throw MaxioUnreadable(ex);
                    }
                }
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "The Maxio request failed.");
        }
        catch (MaxioException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw MaxioUnreachable(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw MaxioTimedOut(ex);
        }
        catch (JsonException ex)
        {
            throw MaxioUnreadable(ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionRecord>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var budget = CreateBudget(cancellationToken);
        var ct = budget.Token;

        try
        {
            var customer = await TryFindCustomerAsync(customerReference, ct);
            if (customer is null)
            {
                // A read must never create a customer; no customer means no subscriptions.
                return Array.Empty<SubscriptionRecord>();
            }

            var customerId = customer.Id
                ?? throw new MaxioApiException(502, "Maxio returned a customer without an identifier.");

            var subscriptions = await _client!.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);

            var records = new List<SubscriptionRecord>();
            foreach (var wrapper in subscriptions)
            {
                if (wrapper.Subscription is { } subscription)
                {
                    records.Add(ToRecord(subscription));
                }
            }

            return records;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "The Maxio request failed.");
        }
        catch (HttpRequestException ex)
        {
            throw MaxioUnreachable(ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw MaxioTimedOut(ex);
        }
        catch (JsonException ex)
        {
            throw MaxioUnreadable(ex);
        }
    }

    private async Task<Customer> GetOrCreateCustomerAsync(SubscriptionCustomerProfile profile, CancellationToken ct)
    {
        var existing = await TryFindCustomerAsync(profile.CustomerReference, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client!.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = profile.FirstName,
                        LastName = profile.LastName,
                        Email = profile.Email,
                        Reference = profile.CustomerReference
                    }
                },
                ct: ct);

            return created.Customer
                ?? throw new MaxioApiException(502, "Maxio returned an empty customer response.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // 422 (including the duplicate-reference restriction) or any other rejection:
            // reconcile with provider state before surfacing anything.
            var afterReconcile = await TryFindCustomerAsync(profile.CustomerReference, ct);
            if (afterReconcile is not null)
            {
                return afterReconcile;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "Maxio rejected the customer request.");
            }

            throw new MaxioApiException(422, "Maxio rejected the customer request.", ex);
        }
        catch (JsonException ex)
        {
            // Unreadable body on the create (success or rejection). Outcome unknown: reconcile.
            var afterReconcile = await TryFindCustomerAsync(profile.CustomerReference, ct);
            if (afterReconcile is not null)
            {
                return afterReconcile;
            }

            throw MaxioUnreadable(ex);
        }
    }

    private async Task<Customer?> TryFindCustomerAsync(string customerReference, CancellationToken ct)
    {
        try
        {
            var response = await _client!.Customers.ReadCustomerByReference(reference: customerReference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<SubscriptionRecord?> FindActiveSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subscriptions = await _client!.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct);

        foreach (var wrapper in subscriptions)
        {
            if (wrapper.Subscription is not { } subscription)
            {
                continue;
            }

            var subscriptionPlanHandle = subscription.Product?.Handle;
            if (!string.Equals(subscriptionPlanHandle, planHandle, StringComparison.Ordinal))
            {
                continue;
            }

            var state = subscription.State?.Value;
            if (state is not null && RetryableStates.Contains(state))
            {
                continue;
            }

            return ToRecord(subscription);
        }

        return null;
    }

    private SubscriptionRecord ToRecord(Subscription subscription)
    {
        return new SubscriptionRecord(
            subscription.Id,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            subscription.Currency,
            subscription.State?.Value ?? "unknown",
            subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt);
    }

    private MaxioApiException TranslateListPlansError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            return new MaxioApiException(
                503,
                $"The Maxio product family '{_options.ProductFamilyHandle}' was not found; check Maxio:ProductFamilyHandle.",
                ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, "Maxio could not list subscription plans.");
        }

        return new MaxioApiException(502, "Maxio could not list subscription plans.", ex);
    }

    private static MaxioApiException TranslateCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errorList)
            && errorList.Errors is { Count: > 0 })
        {
            return new MaxioApiException(422, string.Join("; ", errorList.Errors), ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRaw(raw, "Maxio rejected the subscription request.");
        }

        return new MaxioApiException(422, "Maxio rejected the subscription request.", ex);
    }

    private static MaxioApiException TranslateRaw(RawError raw, string fallback)
    {
        var status = (int)raw.StatusCode;
        var effectiveStatus = status is >= 400 and < 500 ? status : 502;
        var message = effectiveStatus == status ? fallback : "Maxio is unavailable.";
        return new MaxioApiException(effectiveStatus, message);
    }

    private static MaxioApiException MaxioUnreachable(Exception inner) =>
        new(502, "Maxio is unreachable.");

    private static MaxioApiException MaxioTimedOut(Exception inner) =>
        new(504, "The Maxio request timed out.");

    private static MaxioApiException MaxioUnreadable(Exception inner) =>
        new(502, "Maxio returned a response that could not be processed.");

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured || _client is null)
        {
            throw new MaxioNotConfiguredException(
                "Maxio is not configured. Set Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle (from MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN and MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }
    }

    private CancellationTokenSource CreateBudget(CancellationToken cancellationToken)
    {
        var budget = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds > 0 ? _options.RequestTimeoutSeconds : 30);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(budget);
        return cts;
    }
}
