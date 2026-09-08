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
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Subscription service backed by the Maxio Advanced Billing SDK. This is the application's single
/// integration boundary with Maxio: every SDK call in the codebase flows through this class, every
/// call is guarded, and SDK/transport failures are translated to <see cref="MaxioApiException"/> or
/// <see cref="MaxioConfigurationException"/> before they can reach an endpoint.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const string CustomerReferencePrefix = "eshop-";
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(90);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IOptions<MaxioSettings> _options;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AsyncKeyedLock _keyedLock;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> options,
        UserManager<ApplicationUser> userManager,
        AsyncKeyedLock keyedLock,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options;
        _userManager = userManager;
        _keyedLock = keyedLock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListSubscriptionPlansAsync(CancellationToken cancellationToken)
    {
        var settings = GetSettings();
        var familyId = await GetFamilyIdAsync(settings, cancellationToken);
        var products = await ListAllProductsInFamilyAsync(familyId, cancellationToken);

        var plans = new List<SubscriptionPlanInfo>(products.Count);
        foreach (var productResponse in products)
        {
            if (productResponse.Product is not { } product)
            {
                continue;
            }

            plans.Add(MapPlan(product));
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(string loginName, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(loginName))
        {
            throw new MaxioApiException(400, "The caller identity could not be determined from the token.", null);
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioApiException(400, "planHandle is required.", null);
        }

        GetSettings();
        var reference = BuildCustomerReference(loginName);

        // Serialize the check-then-create window per customer reference so a double-click never
        // produces two customers/subscriptions within this host.
        await using (await _keyedLock.LockAsync(reference, cancellationToken))
        {
            var customer = await EnsureCustomerAsync(reference, loginName, cancellationToken);
            var customerId = customer.Id
                ?? throw new MaxioApiException(502, "Maxio returned a customer without an id.", null);

            var existing = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "User {LoginName} already has a live subscription to plan {PlanHandle}; returning existing Maxio subscription {SubscriptionId}.",
                    loginName, planHandle, existing.Id);

                return new SubscribeResult(MapSubscription(existing, planHandle), Created: false);
            }

            // Resolve the plan first so an unknown handle fails fast (400) before we attempt a
            // create, and so the deferred first billing date matches the plan's real interval.
            var plan = await FindPlanByHandleAsync(planHandle, cancellationToken);

            // Defer the first charge to the end of the plan's first billing interval: the plans are
            // seeded payment-method-not-required, so an immediate collection attempt (the Maxio
            // default when next_billing_at is omitted) is rejected with "No payment method was on
            // file". With next_billing_at set, Maxio captures no payment at signup and attempts the
            // first collection near that date.
            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = plan.Handle ?? planHandle,
                    CustomerReference = reference,
                    NextBillingAt = ComputeDeferredFirstBilling(plan)
                }
            };

            try
            {
                var created = await BoundedCallAsync(
                    ct => _client.Subscriptions.CreateSubscription(request, ct),
                    cancellationToken);

                if (created.Subscription is { } createdSubscription)
                {
                    _logger.LogInformation(
                        "Created Maxio subscription {SubscriptionId} for user {LoginName} on plan {PlanHandle}.",
                        createdSubscription.Id, loginName, planHandle);

                    return new SubscribeResult(MapSubscription(createdSubscription, planHandle), Created: true);
                }

                // 2xx with no/undeserializable body: the outcome is unknown, so reconcile.
                var reconciled = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
                if (reconciled is not null)
                {
                    return new SubscribeResult(MapSubscription(reconciled, planHandle), Created: false);
                }

                throw new MaxioApiException(502, "Maxio did not confirm the created subscription.", null);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                // Deterministic provider rejection (e.g. an unknown product handle). No reconcile.
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    var detail = errorList.Errors is { Count: > 0 }
                        ? string.Join(" ", errorList.Errors)
                        : "the subscription request was rejected";
                    throw new MaxioApiException(400, $"Maxio rejected the subscription: {detail}", ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw TranslateRaw(raw, "creating the subscription", ex);
                }

                throw new MaxioApiException(502, "Maxio could not create the subscription.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                // A transport failure may have been retried by the SDK, so the subscription may
                // actually exist. Reconcile instead of assuming nothing happened.
                var reconciled = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
                if (reconciled is not null)
                {
                    _logger.LogWarning(ex,
                        "Create-subscription outcome was ambiguous for user {LoginName} on plan {PlanHandle}; reconciled to existing Maxio subscription {SubscriptionId}.",
                        loginName, planHandle, reconciled.Id);

                    return new SubscribeResult(MapSubscription(reconciled, planHandle), Created: false);
                }

                throw TranslateTransport(ex, "creating the subscription");
            }
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListMySubscriptionsAsync(string loginName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(loginName))
        {
            throw new MaxioApiException(400, "The caller identity could not be determined from the token.", null);
        }

        GetSettings();
        var reference = BuildCustomerReference(loginName);

        var customer = await TryReadCustomerAsync(reference, cancellationToken);
        if (customer is null)
        {
            // The caller has never subscribed, so no Maxio customer exists yet.
            return Array.Empty<SubscriptionInfo>();
        }

        var customerId = customer.Id
            ?? throw new MaxioApiException(502, "Maxio returned a customer without an id.", null);

        var subscriptions = await GuardedReadAsync(
            ct => _client.Customers.ListCustomerSubscriptions(customerId, ct),
            "reading the customer's subscriptions",
            cancellationToken);

        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!, fallbackPlanHandle: null))
            .ToList();
    }

    private async Task<int> GetFamilyIdAsync(MaxioSettings settings, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families = await GuardedReadAsync(
            ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct),
            "listing product families",
            cancellationToken);

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null
                && string.Equals(f.Handle, settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is not int familyId)
        {
            throw new MaxioConfigurationException(
                $"No Maxio product family with handle '{settings.ProductFamilyHandle}' was found on site '{settings.Subdomain}'. Check Maxio:ProductFamilyHandle.");
        }

        return familyId;
    }

    private async Task<Product> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken)
    {
        var settings = GetSettings();
        var familyId = await GetFamilyIdAsync(settings, cancellationToken);
        var products = await ListAllProductsInFamilyAsync(familyId, cancellationToken);

        foreach (var productResponse in products)
        {
            if (productResponse.Product is { } product
                && string.Equals(product.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            {
                return product;
            }
        }

        throw new MaxioApiException(400, $"Unknown subscription plan handle '{planHandle}'.", null);
    }

    private async Task<IReadOnlyList<ProductResponse>> ListAllProductsInFamilyAsync(int familyId, CancellationToken cancellationToken)
    {
        const int perPage = 100;
        var all = new List<ProductResponse>();

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await BoundedCallAsync(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
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
                        ct: ct),
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var message))
                {
                    throw new MaxioApiException(502,
                        $"Maxio could not list plans for the product family (HTTP 404): {message}", ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw TranslateRaw(raw, "listing plans", ex);
                }

                throw new MaxioApiException(502, "Maxio could not list plans.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw TranslateTransport(ex, "listing plans");
            }

            all.AddRange(batch);
            if (batch.Count < perPage)
            {
                break;
            }
        }

        return all;
    }

    private async Task<Customer> EnsureCustomerAsync(string reference, string loginName, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = await ResolveContactAsync(loginName, cancellationToken);
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = contact.FirstName,
                LastName = contact.LastName,
                Email = contact.Email,
                Reference = reference
            }
        };

        try
        {
            var created = await BoundedCallAsync(
                ct => _client.Customers.CreateCustomer(request, ct),
                cancellationToken);

            if (created.Customer is { } customer)
            {
                _logger.LogInformation("Created Maxio customer {CustomerId} for user {LoginName}.", customer.Id, loginName);
                return customer;
            }

            var retried = await TryReadCustomerAsync(reference, cancellationToken);
            if (retried is not null)
            {
                return retried;
            }

            throw new MaxioApiException(502, "Maxio did not confirm the created customer.", null);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here is normally the unique-reference race being lost (two concurrent creates
            // for the same reference). The typed error body does not model the message, so resolve
            // by re-lookup, never by parsing the error text.
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var retried = await TryReadCustomerAsync(reference, cancellationToken);
                if (retried is not null)
                {
                    return retried;
                }

                throw new MaxioApiException(400, "Maxio could not create the customer for this user.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "creating the customer", ex);
            }

            throw new MaxioApiException(502, "Maxio could not create the customer.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            // Ambiguous create outcome: re-read by reference; if it now exists another attempt won.
            var reconciled = await TryReadCustomerAsync(reference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw TranslateTransport(ex, "creating the customer");
        }
    }

    private async Task<Customer?> TryReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedCallAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct),
                cancellationToken);

            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "reading the customer", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw TranslateTransport(ex, "reading the customer");
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await GuardedReadAsync(
            ct => _client.Customers.ListCustomerSubscriptions(customerId, ct),
            "reading the customer's subscriptions",
            cancellationToken);

        foreach (var response in subscriptions)
        {
            var subscription = response.Subscription;
            if (subscription is null)
            {
                continue;
            }

            if (!string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsFinalState(subscription.State))
            {
                return subscription;
            }
        }

        return null;
    }

    private async Task<T> GuardedReadAsync<T>(Func<CancellationToken, Task<T>> read, string what, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedCallAsync(read, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, what, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw TranslateTransport(ex, what);
        }
    }

    /// <summary>
    /// Runs a single Maxio SDK call under a whole-call budget (the SDK's own knobs are per-attempt).
    /// </summary>
    private static async Task<T> BoundedCallAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private MaxioSettings GetSettings()
    {
        var settings = _options.Value;
        if (!settings.HasCredentials)
        {
            throw new MaxioConfigurationException(
                "Maxio billing is not configured. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl) before calling the subscription endpoints.");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio billing is not configured: Maxio:ProductFamilyHandle is missing.");
        }

        return settings;
    }

    private static string BuildCustomerReference(string loginName)
        => CustomerReferencePrefix + loginName.Trim().ToLowerInvariant();

    private async Task<CustomerContact> ResolveContactAsync(string loginName, CancellationToken cancellationToken)
    {
        ApplicationUser? user = null;
        try
        {
            user = await _userManager.FindByNameAsync(loginName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve the identity record for {LoginName}; deriving contact details from the login name.", loginName);
        }

        string email;
        if (!string.IsNullOrWhiteSpace(user?.Email))
        {
            email = user.Email;
        }
        else if (LooksLikeEmail(loginName))
        {
            email = loginName;
        }
        else
        {
            throw new MaxioApiException(
                400,
                $"Cannot determine an email address for caller '{loginName}'. The token must carry the user's login name (email).",
                null);
        }

        var (firstName, lastName) = DeriveNameParts(email);
        return new CustomerContact(email, firstName, lastName);
    }

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at < value.Length - 1 && !value.Any(char.IsWhiteSpace);
    }

    /// <summary>
    /// eShopOnWeb's Identity model does not store first/last names (the token carries only the login
    /// name), so a deterministic display name is derived from the user's email address. The result is
    /// stable across calls, which keeps find-or-create idempotent.
    /// </summary>
    private static (string FirstName, string LastName) DeriveNameParts(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        var domain = at >= 0 && at < email.Length - 1 ? email[(at + 1)..] : string.Empty;

        string first;
        string last;
        var dot = local.IndexOf('.');
        if (dot > 0 && dot < local.Length - 1)
        {
            first = local[..dot];
            last = local[(dot + 1)..];
        }
        else
        {
            first = local;
            var domainDot = domain.IndexOf('.');
            last = domainDot > 0 ? domain[..domainDot] : domain;
        }

        if (string.IsNullOrWhiteSpace(first))
        {
            first = "Subscriber";
        }

        if (string.IsNullOrWhiteSpace(last))
        {
            last = "Member";
        }

        return (first.Trim(), last.Trim());
    }

    private static bool IsFinalState(SubscriptionState? state)
        => state == SubscriptionState.Canceled
            || state == SubscriptionState.Expired
            || state == SubscriptionState.TrialEnded
            || state == SubscriptionState.FailedToCreate;

    private static SubscriptionPlanInfo MapPlan(Product product)
        => new SubscriptionPlanInfo(
            Handle: product.Handle ?? string.Empty,
            Name: product.Name ?? string.Empty,
            PriceAmount: ToMajor(product.PriceInCents) ?? 0m,
            Interval: product.Interval,
            IntervalUnit: product.IntervalUnit?.Value);

    /// <summary>
    /// The first billing date used to defer the initial charge: the end of the plan's first billing
    /// interval, based on the plan's own interval/interval-unit (works for any catalog, not just the
    /// seeded monthly plans).
    /// </summary>
    private static DateTimeOffset ComputeDeferredFirstBilling(Product product)
    {
        var interval = product.Interval is > 0 ? product.Interval.Value : 1;
        var now = DateTimeOffset.UtcNow;

        return string.Equals(product.IntervalUnit?.Value, "day", StringComparison.OrdinalIgnoreCase)
            ? now.AddDays(interval)
            : now.AddMonths(interval);
    }

    private static SubscriptionInfo MapSubscription(Subscription subscription, string? fallbackPlanHandle)
        => new SubscriptionInfo(
            Id: subscription.Id,
            PlanHandle: subscription.Product?.Handle ?? fallbackPlanHandle,
            PlanName: subscription.Product?.Name,
            PriceAmount: ToMajor(subscription.ProductPriceInCents),
            Currency: subscription.Currency,
            State: subscription.State?.Value,
            NextBillingDate: subscription.CurrentPeriodEndsAt);

    private static decimal? ToMajor(long? cents)
        => cents.HasValue ? cents.Value / 100m : null;

    private MaxioApiException TranslateRaw(RawError raw, string what, Exception inner)
    {
        var status = (int)raw.StatusCode;
        var detail = TryReadBody(raw);

        // Provider 4xx responses that reflect an invalid caller request pass through with their own
        // status. 401/403 and anything else (5xx, unexpected 404/429...) are upstream failures.
        var mappedStatus = status switch
        {
            400 or 409 or 422 => status,
            _ => 502
        };

        var message = string.IsNullOrWhiteSpace(detail)
            ? $"Maxio {what} failed (HTTP {status})."
            : $"Maxio {what} failed (HTTP {status}): {detail}";

        return new MaxioApiException(mappedStatus, message, inner);
    }

    private static string? TryReadBody(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            return body.Length <= 300 ? body : body[..300] + "...";
        }
        catch
        {
            return null;
        }
    }

    private MaxioApiException TranslateTransport(Exception ex, string what)
    {
        if (ex is OperationCanceledException)
        {
            return new MaxioApiException(504, $"Maxio {what} timed out.", ex);
        }

        return new MaxioApiException(502, $"Maxio is unreachable while {what}.", ex);
    }
}
