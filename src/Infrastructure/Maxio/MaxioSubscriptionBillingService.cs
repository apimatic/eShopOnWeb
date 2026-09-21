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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> implemented against the Maxio Advanced Billing SDK.
/// Owns every SDK interaction and translates every provider failure into a single
/// <see cref="SubscriptionBillingException"/> so no SDK type escapes this boundary.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // Subscription states that count as "already subscribed" for idempotency — a live or
    // transient-to-live subscription to the same plan must not be duplicated.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        SubscriptionState.Active.Value,
        SubscriptionState.Trialing.Value,
        SubscriptionState.Pending.Value,
        SubscriptionState.Assessing.Value
    };

    // Per-user gate so a double-click (or overlapping requests) serializes the
    // read-customer / list-subscriptions / create sequence within this process. See the plan's
    // idempotency decision: this holds within a single run; Maxio's customer `reference` is the
    // durable idempotency anchor across restarts.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new();

    // One deadline for a whole public operation (which may make several SDK calls). The SDK's own
    // Timeout is per-attempt, so a CancellationToken is the only thing that bounds the whole call.
    private static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(45);

    private const int PageSize = 200;
    private const int MaxPages = 25;

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

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = await ExecuteAsync("List subscription plans", cancellationToken, async ct =>
        {
            var results = new List<SubscriptionPlanInfo>();
            try
            {
                for (int page = 1; page <= MaxPages; page++)
                {
                    IReadOnlyList<ProductResponse> pageProducts = await _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: FormatFamilyReference(_settings.ProductFamilyHandle),
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

                    results.AddRange(pageProducts.Select(p => MapPlan(p.Product)));

                    if (pageProducts.Count < PageSize)
                    {
                        break; // last (short) page — provider-side stop, bounded by MaxPages above.
                    }
                }
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw TranslateProductFamilyError(ex);
            }

            return results;
        });

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (subscriber is null) throw new ArgumentNullException(nameof(subscriber));
        if (string.IsNullOrWhiteSpace(planHandle)) throw new PlanNotFoundException(planHandle ?? string.Empty);

        // Enforce the cross-operation invariant: the target plan must be one the plan list returns.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new PlanNotFoundException(planHandle);
        }

        var gate = UserGates.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);
            var customerId = customer.Id!.Value;

            // Idempotency: reuse an existing live subscription to the same plan instead of creating a duplicate.
            var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscribe replay for customer {CustomerId} plan {PlanHandle}: existing subscription {SubscriptionId} reused.",
                    customerId, plan.Handle, existing.Id);
                return new SubscribeResult(MapSubscription(existing), alreadyExisted: true);
            }

            var created = await CreateSubscriptionAsync(customerId, plan.Handle, cancellationToken);
            _logger.LogInformation(
                "Subscription {SubscriptionId} created for customer {CustomerId} plan {PlanHandle} (state {State}).",
                created.Id, customerId, plan.Handle, created.State?.Value);
            return new SubscribeResult(MapSubscription(created), alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        if (subscriber is null) throw new ArgumentNullException(nameof(subscriber));

        var customer = await TryReadCustomerAsync(subscriber.Reference, cancellationToken);
        if (customer?.Id is not int customerId)
        {
            return Array.Empty<SubscriptionInfo>(); // no Maxio customer yet ⇒ no subscriptions.
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    // --- private SDK helpers (each translates its own provider failures) ---

    private async Task<Customer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerAsync(subscriber.Reference, cancellationToken);
        if (existing is not null && existing.Id.HasValue)
        {
            return existing;
        }

        var created = await ExecuteAsync("Create customer", cancellationToken, async ct =>
        {
            try
            {
                var response = await _client.Customers.CreateCustomer(new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = subscriber.FirstName,
                        LastName = subscriber.LastName,
                        Email = subscriber.Email,
                        // Links the eShop user to their Maxio customer — the durable idempotency anchor.
                        Reference = subscriber.Reference
                    }
                }, ct: ct);
                return response.Customer;
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                throw TranslateCreateCustomerError(ex);
            }
        });

        _logger.LogInformation("Maxio customer {CustomerId} ensured for reference {Reference}.", created.Id, subscriber.Reference);
        return created;
    }

    private Task<Customer?> TryReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        return ExecuteAsync<Customer?>("Look up customer", cancellationToken, async ct =>
        {
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
                return response.Customer;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null; // customer does not exist yet — an expected miss, NOT an error.
            }
        });
    }

    private Task<Subscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        return ExecuteAsync<Subscription?>("Find existing subscription", cancellationToken, async ct =>
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
            return subscriptions
                .Select(s => s.Subscription)
                .FirstOrDefault(s => s is not null
                    && string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                    && IsLiveState(s.State));
        });
    }

    private Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        return ExecuteAsync("List customer subscriptions", cancellationToken,
            ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct));
    }

    private async Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscription = await ExecuteAsync("Create subscription", cancellationToken, async ct =>
        {
            try
            {
                var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        CustomerId = customerId,
                        ProductHandle = planHandle
                        // PaymentCollectionMethod omitted → provider default; plans are configured as
                        // payment-method-not-required so Maxio activates the subscription without a card.
                    }
                }, ct: ct);
                return response.Subscription;
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                throw TranslateCreateSubscriptionError(ex);
            }
        });

        if (subscription is null)
        {
            throw new SubscriptionBillingException(
                "The billing provider accepted the subscription but returned no subscription details.", null);
        }

        return subscription;
    }

    // --- error translation (keeps the discriminator — status or provider message — that the caller mapping needs) ---

    private static SubscriptionBillingException TranslateProductFamilyError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            // 404 — the configured product family handle was not found on the site.
            return new SubscriptionBillingException(
                $"Configured product family was not found: {message}", 404, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw("List subscription plans", raw, ex);
        }
        return new SubscriptionBillingException("List subscription plans failed (unrecognised provider error).", null, ex);
    }

    private static SubscriptionBillingException TranslateCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var body))
        {
            return new SubscriptionBillingException(
                $"Customer could not be created: {DescribeCustomerErrors(body)}", 422, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw("Create customer", raw, ex);
        }
        return new SubscriptionBillingException("Create customer failed (unrecognised provider error).", null, ex);
    }

    private static SubscriptionBillingException TranslateCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var body))
        {
            var detail = body.Errors is { Count: > 0 } ? string.Join("; ", body.Errors) : "validation failed";
            return new SubscriptionBillingException($"Subscription could not be created: {detail}", 422, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw("Create subscription", raw, ex);
        }
        return new SubscriptionBillingException("Create subscription failed (unrecognised provider error).", null, ex);
    }

    private static string DescribeCustomerErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
        {
            return "validation failed";
        }
        if (body.Errors.TryGetListOfString(out var list) && list is { Count: > 0 })
        {
            return string.Join("; ", list);
        }
        if (body.Errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
        {
            return customerError.Customer!;
        }
        return "validation failed";
    }

    private static SubscriptionBillingException FromRaw(string action, RawError raw, Exception inner)
    {
        var status = (int)raw.StatusCode;
        return new SubscriptionBillingException($"{action} failed with HTTP {status}.", status, inner);
    }

    /// <summary>
    /// Central boundary: runs an SDK call under a whole-operation deadline and converts every SDK /
    /// transport / decode failure into <see cref="SubscriptionBillingException"/>. Already-translated
    /// exceptions (typed Case-A errors handled inside the call) pass through unchanged.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(string action, CancellationToken callerToken, Func<CancellationToken, Task<T>> call)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(OperationBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (SubscriptionBillingException)
        {
            throw; // typed Case-A error already translated at the call site.
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(action, ex.Error, ex);
        }
        catch (JsonException ex)
        {
            // Two distinct JsonException directions both land here: a drifted 2xx body, and a non-2xx
            // body that did not match its generated error shape (which destroys the status). Either way
            // the detail is unusable — surface a caller-safe message, never the JSON path.
            throw new SubscriptionBillingException(
                $"{action}: the billing provider returned a response that could not be processed.", null, ex);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw; // the caller (or client) cancelled — propagate real cancellation.
        }
        catch (OperationCanceledException ex)
        {
            // Our own operation budget elapsed — a timeout, not a caller cancellation.
            throw new SubscriptionBillingException($"{action}: the billing provider did not respond in time.", 504, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SubscriptionBillingException($"{action}: the billing provider is unreachable.", null, ex);
        }
    }

    // --- mapping SDK models → transport-neutral DTOs ---

    private static SubscriptionPlanInfo MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        ProductId = product.Id
    };

    private static SubscriptionInfo MapSubscription(Subscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        State = subscription.State?.Value,
        PriceInCents = subscription.ProductPriceInCents,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };

    private static bool IsLiveState(SubscriptionState? state)
        => state?.Value is { } value && LiveStates.Contains(value);

    /// <summary>
    /// The product-family-scoped endpoints take <c>product_family_id</c> as either a numeric id or a
    /// handle prefixed with <c>handle:</c> (per the SDK's <c>productFamilyId</c> param docs). A bare
    /// handle 404s, so prefix a non-numeric, not-already-prefixed value.
    /// </summary>
    private static string FormatFamilyReference(string configuredFamily)
    {
        var value = configuredFamily.Trim();
        if (value.StartsWith("handle:", StringComparison.OrdinalIgnoreCase) || value.All(char.IsDigit))
        {
            return value;
        }
        return "handle:" + value;
    }
}
