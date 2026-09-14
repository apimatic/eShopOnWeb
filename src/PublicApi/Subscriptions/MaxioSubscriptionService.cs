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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionService"/>.
/// Idempotency model: the Maxio customer is keyed by a deterministic per-user reference (the signed-in
/// email), so no local persistence is required and double-clicks never create duplicate customers or
/// subscriptions. Subscription creation additionally carries a deterministic subscription reference and
/// is guarded by an in-process per-user lock; a rejected/ambiguous create is settled by re-reading the
/// customer's subscriptions rather than assuming failure.
/// </summary>
public sealed class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new(StringComparer.OrdinalIgnoreCase);

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, MaxioOptions options, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return CallAsync("list the subscription plans", async token =>
        {
            var plans = new List<SubscriptionPlanInfo>();
            const int perPage = 200;
            int page = 1;

            while (true)
            {
                IReadOnlyList<ProductResponse> products;
                const string operation = "list the subscription plans";
                try
                {
                    products = await _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: "handle:" + _options.ProductFamilyHandle,
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
                        ct: token);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    if (ex.Error.TryGetString(out _))
                    {
                        throw MaxioSubscriptionException.ProviderRejected(
                            StatusCodes.Status404NotFound,
                            $"The configured product family '{_options.ProductFamilyHandle}' was not found.", ex);
                    }
                    else if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw TranslateRaw(raw, operation, ex);
                    }

                    throw MaxioSubscriptionException.ProviderError(operation, ex);
                }

                foreach (var productResponse in products)
                {
                    var product = productResponse.Product;
                    if (product is null || product.Handle is null)
                    {
                        continue;
                    }

                    plans.Add(new SubscriptionPlanInfo(
                        product.Handle,
                        product.Name,
                        ToMoney(product.PriceInCents),
                        product.Interval,
                        product.IntervalUnit?.Value,
                        product.RequireCreditCard));
                }

                if (products.Count < perPage)
                {
                    break;
                }

                page++;
            }

            return (IReadOnlyList<SubscriptionPlanInfo>)plans;
        }, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(string customerReference, string productHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw MaxioSubscriptionException.BadRequest("A customer reference is required.");
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw MaxioSubscriptionException.BadRequest("A productHandle is required.");
        }

        var gate = _userLocks.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeCoreAsync(customerReference, productHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return Array.Empty<SubscriptionInfo>();
        }

        var customer = await FindCustomerAsync(customerReference, cancellationToken);
        if (customer is null || customer.Id is not int customerId)
        {
            return Array.Empty<SubscriptionInfo>();
        }

        return await ListCustomerSubscriptionsCoreAsync(customerId, cancellationToken);
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(string customerReference, string productHandle, CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(customerReference, cancellationToken);
        if (customer.Id is not int customerId)
        {
            throw MaxioSubscriptionException.UnreadableResponse("confirm the billing customer");
        }

        // Detection before creation: a double-click (or a retry after an ambiguous failure) must never
        // create a second subscription for the same plan while one is still live.
        var existing = await FindEnrollmentAsync(customerId, productHandle, cancellationToken);
        if (existing is not null)
        {
            return new SubscribeResult(true, existing);
        }

        var plan = await FindPlanAsync(productHandle, cancellationToken);
        if (plan is null)
        {
            throw MaxioSubscriptionException.PlanNotFound(productHandle);
        }

        if (plan.RequiresCreditCard == true)
        {
            throw MaxioSubscriptionException.BadRequest(
                $"The plan '{productHandle}' requires a payment method; this capability only supports card-less plans.");
        }

        // Card-less enrollment: with no payment method on file, Maxio collects the first-period charge at
        // signup and refuses to create the subscription when that collection cannot run. Scheduling the
        // first billing one interval in the future (a documented create-time attribute) means no payment is
        // captured at creation; the first collection is attempted at the returned next-billing date instead.
        var subscription = await CreateEnrollmentAsync(
            customerId, customerReference, productHandle, CalculateNextBillingAt(plan), cancellationToken);
        return new SubscribeResult(false, subscription);
    }

    private static DateTimeOffset CalculateNextBillingAt(SubscriptionPlanInfo plan)
    {
        var now = DateTimeOffset.UtcNow;
        var interval = Math.Max(1, plan.Interval ?? 1);

        return string.Equals(plan.IntervalUnit, "day", StringComparison.OrdinalIgnoreCase)
            ? now.AddDays(interval)
            : now.AddMonths(interval);
    }

    private async Task<SubscriptionPlanInfo?> FindPlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Customer?> FindCustomerAsync(string customerReference, CancellationToken cancellationToken)
    {
        const string operation = "look up the billing customer";
        return await CallAsync(operation, async token =>
        {
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(customerReference, ct: token);
                return response.Customer;
            }
            catch (SdkException<RawError> ex)
            {
                if (ex.Error.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw TranslateRaw(ex.Error, operation, ex);
            }
        }, cancellationToken);
    }

    private async Task<Customer> EnsureCustomerAsync(string customerReference, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(customerReference);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = customerReference,
                Reference = customerReference
            }
        };

        const string operation = "create the billing customer";
        return await CallAsync(operation, async token =>
        {
            try
            {
                var response = await _client.Customers.CreateCustomer(body, ct: token);
                return response.Customer;
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                // A 422 is most often the unique-reference rejection when the customer was created by a
                // concurrent request. Settle by re-reading rather than failing.
                var reconcile = await FindCustomerSafeAsync(customerReference, token);
                if (reconcile is not null)
                {
                    return reconcile;
                }

                if (ex.Error.TryGetCustomerErrorResponse1(out _))
                {
                    throw MaxioSubscriptionException.ProviderRejected(
                        StatusCodes.Status400BadRequest,
                        "The billing provider rejected the customer record; verify the account details.", ex);
                }
                else if (ex.Error.TryGetRawError(out var raw))
                {
                    throw TranslateRaw(raw, operation, ex);
                }

                throw MaxioSubscriptionException.ProviderError(operation, ex);
            }
            catch (HttpRequestException)
            {
                // Ambiguous write: the request may have reached Maxio even though the connection failed.
                var reconcile = await FindCustomerSafeAsync(customerReference, token);
                if (reconcile is not null)
                {
                    return reconcile;
                }

                throw;
            }
            catch (JsonException)
            {
                // A 2xx whose body could not be read is also ambiguous.
                var reconcile = await FindCustomerSafeAsync(customerReference, token);
                if (reconcile is not null)
                {
                    return reconcile;
                }

                throw;
            }
        }, cancellationToken);
    }

    private async Task<Customer?> FindCustomerSafeAsync(string customerReference, CancellationToken cancellationToken)
    {
        try
        {
            return await FindCustomerAsync(customerReference, cancellationToken);
        }
        catch (MaxioSubscriptionException)
        {
            return null;
        }
    }

    private async Task<SubscriptionInfo?> FindEnrollmentAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await ListCustomerSubscriptionsCoreAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase)
            && !IsTerminalState(s.State));
    }

    private async Task<SubscriptionInfo> CreateEnrollmentAsync(
        int customerId, string customerReference, string productHandle, DateTimeOffset nextBillingAt, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = $"{customerReference}:{productHandle}",
                NextBillingAt = nextBillingAt
            }
        };

        const string operation = "create the subscription";
        try
        {
            return await CallAsync(operation, async token =>
            {
                try
                {
                    var response = await _client.Subscriptions.CreateSubscription(body, ct: token);
                    var subscription = response.Subscription;
                    if (subscription is null)
                    {
                        throw MaxioSubscriptionException.UnreadableResponse(operation);
                    }

                    return ToSubscriptionInfo(subscription);
                }
                catch (SdkException<CreateSubscriptionError> ex)
                {
                    if (ex.Error.TryGetErrorListResponse1(out var errorList))
                    {
                        var reconcile = await FindEnrollmentSafeAsync(customerId, productHandle, token);
                        if (reconcile is not null)
                        {
                            return reconcile;
                        }

                        var detail = errorList.Errors is { Count: > 0 }
                            ? string.Join("; ", errorList.Errors)
                            : "The billing provider rejected the subscription request.";
                        throw MaxioSubscriptionException.ProviderRejected(StatusCodes.Status400BadRequest, detail, ex);
                    }
                    else if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw TranslateRaw(raw, operation, ex);
                    }

                    throw MaxioSubscriptionException.ProviderError(operation, ex);
                }
            }, cancellationToken);
        }
        catch (MaxioSubscriptionException ex) when (ex.StatusCode >= 500)
        {
            // Ambiguous write (transport failure or unreadable 2xx): the subscription may have been
            // created server-side before the response was lost. Settle by re-reading.
            var reconcile = await FindEnrollmentSafeAsync(customerId, productHandle, cancellationToken);
            if (reconcile is not null)
            {
                return reconcile;
            }

            throw;
        }
    }

    private async Task<SubscriptionInfo?> FindEnrollmentSafeAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        try
        {
            return await FindEnrollmentAsync(customerId, productHandle, cancellationToken);
        }
        catch (MaxioSubscriptionException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<SubscriptionInfo>> ListCustomerSubscriptionsCoreAsync(int customerId, CancellationToken cancellationToken)
    {
        const string operation = "list the customer's subscriptions";
        return await CallAsync(operation, async token =>
        {
            try
            {
                var list = await _client.Customers.ListCustomerSubscriptions(customerId, ct: token);
                var subscriptions = new List<SubscriptionInfo>(list.Count);
                foreach (var item in list)
                {
                    var subscription = item.Subscription;
                    if (subscription is not null)
                    {
                        subscriptions.Add(ToSubscriptionInfo(subscription));
                    }
                }

                return (IReadOnlyList<SubscriptionInfo>)subscriptions;
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRaw(ex.Error, operation, ex);
            }
        }, cancellationToken);
    }

    private static SubscriptionInfo ToSubscriptionInfo(Subscription subscription)
    {
        return new SubscriptionInfo(
            subscription.Id,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            ToMoney(subscription.ProductPriceInCents),
            subscription.Currency,
            subscription.State?.Value,
            subscription.CurrentPeriodStartedAt,
            subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt);
    }

    /// <summary>States in which the plan is still 'live' — a fresh subscription must not be created.</summary>
    private static bool IsTerminalState(string? state)
    {
        if (state is null)
        {
            return false;
        }

        return state is "canceled" or "expired" or "failed_to_create";
    }

    private static decimal? ToMoney(long? cents) =>
        cents.HasValue ? Math.Round(cents.Value / 100m, 2) : null;

    private static (string FirstName, string LastName) SplitDisplayName(string email)
    {
        const int maxLength = 60;

        string Trim(string value)
        {
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        var at = email.IndexOf('@');
        if (at <= 0)
        {
            return ("Valued", "Customer");
        }

        var localPart = email.Substring(0, at);
        var domain = email.Substring(at + 1);
        var dot = domain.IndexOf('.');
        var organization = dot > 0 ? domain.Substring(0, dot) : domain;

        return (Trim(localPart), Trim(string.IsNullOrEmpty(organization) ? "Customer" : organization));
    }

    private MaxioSubscriptionException TranslateRaw(RawError raw, string operation, Exception inner)
    {
        var status = (int)raw.StatusCode;
        string body;
        try
        {
            body = raw.ReadAsString() ?? string.Empty;
        }
        catch (Exception)
        {
            body = string.Empty;
        }

        if (status >= 400 && status < 500)
        {
            var detail = string.IsNullOrWhiteSpace(body)
                ? "The billing provider rejected the request."
                : $"The billing provider rejected the request: {Truncate(body)}";
            return MaxioSubscriptionException.ProviderRejected(status, detail, inner);
        }

        _logger.LogError(inner, "Maxio provider error (HTTP {Status}) while trying to {Operation}.", status, operation);
        return MaxioSubscriptionException.ProviderError(operation, inner);
    }

    private static string Truncate(string value)
    {
        const int max = 400;
        return value.Length <= max ? value : value.Substring(0, max) + "...";
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw MaxioSubscriptionException.NotConfigured();
        }
    }

    private async Task<T> CallAsync<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);

        try
        {
            return await call(budget.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw MaxioSubscriptionException.ProviderUnavailable(operation + " (timed out)", ex);
        }
        catch (HttpRequestException ex)
        {
            throw MaxioSubscriptionException.ProviderUnavailable(operation, ex);
        }
        catch (JsonException ex)
        {
            throw MaxioSubscriptionException.UnreadableResponse(operation, ex);
        }
    }
}
