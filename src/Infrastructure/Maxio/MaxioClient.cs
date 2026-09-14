using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="IMaxioSubscriptionService"/>, built to the
/// Maxio OpenAPI contract (maxio-spec/openapi.yaml). Uses a typed <see cref="HttpClient"/> whose
/// base address and HTTP Basic credentials are configured in <see cref="MaxioServiceCollectionExtensions"/>.
/// </summary>
public sealed class MaxioClient : IMaxioSubscriptionService
{
    // Subscription states that are considered "live" for idempotency purposes: an existing
    // subscription in any of these states means the shopper is already enrolled, so a repeat
    // subscribe returns it instead of creating a duplicate. Derived from Subscription-State enum.
    private static readonly HashSet<string> DeadStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    // Serializes concurrent subscribe calls for the same subscriber reference (e.g. a double-click)
    // so the existing-subscription check and the create cannot interleave within a single process.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings, IAppLogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyCollection<SubscriptionPlan>>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // GET /products.json — list all products for the site, then keep only those in the
            // configured product family. The spec's Product-Response carries product_family.handle.
            var products = await GetAsync<List<MaxioProductEnvelope>>("products.json", cancellationToken)
                           ?? new List<MaxioProductEnvelope>();

            var plans = products
                .Select(p => p.Product)
                .Where(p => p is not null)
                .Select(p => p!)
                .Where(p => p.ArchivedAt is null)
                .Where(p => string.Equals(p.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
                .Select(MapPlan)
                .OrderBy(p => p.PriceInCents)
                .ToList();

            return Result<IReadOnlyCollection<SubscriptionPlan>>.Success(plans);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning("Maxio GetPlans failed: {0}", ex.Message);
            return Result<IReadOnlyCollection<SubscriptionPlan>>.Error(ex.Errors.DefaultIfEmpty(ex.Message).ToArray());
        }
    }

    public async Task<Result<CustomerSubscription>> SubscribeAsync(EShopSubscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return Result<CustomerSubscription>.Invalid(new List<ValidationError> { new() { ErrorMessage = "planHandle is required." } });
        }

        var gate = SubscribeLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // 1. Ensure the plan actually exists in the configured family before enrolling.
            var plansResult = await GetPlansAsync(cancellationToken);
            if (!plansResult.IsSuccess)
            {
                return Result<CustomerSubscription>.Error(plansResult.Errors.ToArray());
            }
            if (!plansResult.Value.Any(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
            {
                return Result<CustomerSubscription>.NotFound($"No subscription plan with handle '{planHandle}' exists in product family '{_settings.ProductFamilyHandle}'.");
            }

            // 2. Ensure a Maxio customer exists for this eShop user (idempotent by reference).
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            // 3. Idempotency fast-path: if the customer already has a live subscription on this
            //    plan, return it rather than creating a duplicate.
            var subscriptionReference = BuildSubscriptionReference(subscriber.Reference, planHandle);
            var existing = await FindLiveSubscriptionAsync(customer.Id, planHandle, subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Maxio subscribe: returning existing subscription {0} for {1} on {2}.", existing.Id, subscriber.Reference, planHandle);
                return Result<CustomerSubscription>.Success(MapSubscription(existing, alreadyExisted: true));
            }

            // 4. Create the subscription. A deterministic reference lets Maxio itself reject a
            //    concurrent duplicate (422), which we resolve by looking the subscription back up.
            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscriptionBody
                {
                    ProductHandle = planHandle,
                    CustomerId = customer.Id,
                    Reference = subscriptionReference,
                    // Invoice-based collection: the demo plans require no payment method, so we
                    // avoid an automatic card charge that would otherwise fail with no card on file.
                    PaymentCollectionMethod = "remittance"
                }
            };

            try
            {
                var created = await PostAsync<CreateSubscriptionRequest, MaxioSubscriptionEnvelope>("subscriptions.json", request, cancellationToken);
                var subscription = created?.Subscription
                    ?? throw new InvalidOperationException("Maxio returned an empty subscription payload.");
                _logger.LogInformation("Maxio subscribe: created subscription {0} for {1} on {2}.", subscription.Id, subscriber.Reference, planHandle);
                return Result<CustomerSubscription>.Success(MapSubscription(subscription, alreadyExisted: false));
            }
            catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                // Possible duplicate created by a racing request — resolve via the deterministic reference.
                var recovered = await LookupSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (recovered is not null)
                {
                    return Result<CustomerSubscription>.Success(MapSubscription(recovered, alreadyExisted: true));
                }

                _logger.LogWarning("Maxio subscribe rejected for {0} on {1}: {2}", subscriber.Reference, planHandle, ex.Message);
                return Result<CustomerSubscription>.Invalid(ex.Errors.DefaultIfEmpty(ex.Message)
                    .Select(m => new ValidationError { ErrorMessage = m }).ToList());
            }
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning("Maxio subscribe failed for {0}: {1}", subscriber.Reference, ex.Message);
            return Result<CustomerSubscription>.Error(ex.Errors.DefaultIfEmpty(ex.Message).ToArray());
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Result<IReadOnlyCollection<CustomerSubscription>>> GetSubscriptionsAsync(EShopSubscriber subscriber, CancellationToken cancellationToken = default)
    {
        try
        {
            var customer = await LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (customer is null)
            {
                // No customer record yet — the user simply has no subscriptions.
                return Result<IReadOnlyCollection<CustomerSubscription>>.Success(Array.Empty<CustomerSubscription>());
            }

            var subscriptions = await GetAsync<List<MaxioSubscriptionEnvelope>>($"customers/{customer.Id}/subscriptions.json", cancellationToken)
                                ?? new List<MaxioSubscriptionEnvelope>();

            var mapped = subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!, alreadyExisted: true))
                .OrderByDescending(s => s.CreatedAt)
                .ToList();

            return Result<IReadOnlyCollection<CustomerSubscription>>.Success(mapped);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning("Maxio GetSubscriptions failed for {0}: {1}", subscriber.Reference, ex.Message);
            return Result<IReadOnlyCollection<CustomerSubscription>>.Error(ex.Errors.DefaultIfEmpty(ex.Message).ToArray());
        }
    }

    // -- Customer helpers -----------------------------------------------------------------------

    private async Task<MaxioCustomer> EnsureCustomerAsync(EShopSubscriber subscriber, CancellationToken cancellationToken)
    {
        var existing = await LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerBody
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference
            }
        };

        try
        {
            var created = await PostAsync<CreateCustomerRequest, MaxioCustomerEnvelope>("customers.json", request, cancellationToken);
            var customer = created?.Customer;
            if (customer is not null)
            {
                _logger.LogInformation("Maxio: created customer {0} for reference {1}.", customer.Id, subscriber.Reference);
                return customer;
            }
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request may have created the customer first (reference is unique) — re-read.
            var recovered = await LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }
            throw;
        }

        // Extremely defensive: creation reported success but returned no body; re-read by reference.
        return await LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken)
               ?? throw new InvalidOperationException("Maxio customer creation succeeded but the customer could not be read back.");
    }

    private async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        // GET /customers/lookup.json?reference=... — returns 200 with the customer or 404 when absent.
        var envelope = await GetAsync<MaxioCustomerEnvelope>($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return envelope?.Customer;
    }

    // -- Subscription helpers -------------------------------------------------------------------

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, string subscriptionReference, CancellationToken cancellationToken)
    {
        var subscriptions = await GetAsync<List<MaxioSubscriptionEnvelope>>($"customers/{customerId}/subscriptions.json", cancellationToken)
                            ?? new List<MaxioSubscriptionEnvelope>();

        var candidates = subscriptions.Select(s => s.Subscription).Where(s => s is not null).Select(s => s!).ToList();

        // Prefer an exact match on our deterministic reference, then any live subscription on the plan.
        return candidates.FirstOrDefault(s => string.Equals(s.Reference, subscriptionReference, StringComparison.Ordinal) && IsLive(s.State))
               ?? candidates.FirstOrDefault(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && IsLive(s.State));
    }

    private async Task<MaxioSubscription?> LookupSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        // GET /subscriptions/lookup.json?reference=... — returns 200 with the subscription or 404.
        var envelope = await GetAsync<MaxioSubscriptionEnvelope>($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        return envelope?.Subscription;
    }

    private static bool IsLive(string? state) => !string.IsNullOrEmpty(state) && !DeadStates.Contains(state);

    private static string BuildSubscriptionReference(string subscriberReference, string planHandle)
        => $"eshop:{subscriberReference}:{planHandle}";

    // -- HTTP plumbing --------------------------------------------------------------------------

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string relativeUrl, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(relativeUrl, body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, MaxioApiException.ParseErrors(body));
    }

    // -- Mapping --------------------------------------------------------------------------------

    private SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle,
        PricePointName = product.ProductPricePointName
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, bool alreadyExisted) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        ProductPriceInCents = subscription.ProductPriceInCents,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt,
        Reference = subscription.Reference,
        CustomerId = subscription.Customer?.Id ?? 0,
        AlreadyExisted = alreadyExisted
    };
}
