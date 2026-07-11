using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;
using Subscription = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single Infrastructure class that talks to Maxio Advanced Billing (via the maxio-sdk plugin's
/// generated <see cref="MaxioAdvancedBillingClient"/>). Nothing else in eShopOnWeb touches the provider
/// directly - this class implements the provider-agnostic <see cref="IBillingClient"/> seam, resolves the
/// outbound base URL from <see cref="MaxioSettings"/> (plan.md §2.3/§4.3), and translates every provider
/// error into an ApplicationCore exception.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    // Process-wide cache: the configured metered component handle doesn't change at runtime, so once
    // validated (or found invalid) there is no need to re-check it on every usage call.
    private static readonly ConcurrentDictionary<string, bool> MeteredComponentValidated = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> options)
    {
        _settings = options.Value;

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = _settings.ApiKey ?? string.Empty, Password = "x" },
            Environment = string.Equals(_settings.Environment, "EU", StringComparison.OrdinalIgnoreCase)
                ? ServerEnvironment.Eu
                : ServerEnvironment.Us,
        };

        // Resolution order (plan.md §2.3): an explicit Maxio:BaseUrl always wins, verbatim, over the
        // subdomain-derived host - this is what lets the identical build target prod/dev/a local mock
        // purely through configuration.
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            if (clientOptions.Environment == ServerEnvironment.Eu)
            {
                clientOptions.Server.Production.Eu.BaseUrl = _settings.BaseUrl;
            }
            else
            {
                clientOptions.Server.Production.Us.BaseUrl = _settings.BaseUrl;
            }
        }
        else
        {
            if (clientOptions.Environment == ServerEnvironment.Eu)
            {
                clientOptions.Server.Production.Eu.Site = _settings.Subdomain ?? string.Empty;
            }
            else
            {
                clientOptions.Server.Production.Us.Site = _settings.Subdomain ?? string.Empty;
            }
        }

        _client = new MaxioAdvancedBillingClient(httpClient, clientOptions);
    }

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: _settings.ProductFamilyId.ToString(CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: 1,
                perPage: 50,
                ct: cancellationToken);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            var message = ex.Error.TryGetString(out var notFound) ? notFound : DescribeRawFallback(ex.Error);
            throw new BillingProviderException($"Failed to list plans for product family '{_settings.ProductFamilyHandle}': {message}", ex);
        }

        return products
            .Select(p => p.Product)
            .Where(p => p is not null)
            .Select(p => new BillingPlan(
                p!.Id ?? 0,
                p.Handle ?? string.Empty,
                p.Name ?? string.Empty,
                p.PriceInCents ?? 0,
                p.IntervalUnit?.Value ?? "month",
                p.Interval ?? 1))
            .ToList();
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await TryReadCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return MapCustomer(existing);
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = customerReference,
                },
            }, cancellationToken);

            return MapCustomer(created.Customer);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The reference is unique-constrained - a concurrent request may have created it first.
            var retry = await TryReadCustomerByReferenceAsync(customerReference, cancellationToken);
            if (retry is not null)
            {
                return MapCustomer(retry);
            }

            var message = ex.Error.TryGetCustomerErrorResponse1(out var validation)
                ? validation.ToString()
                : DescribeRawFallback(ex.Error);
            throw new BillingProviderException($"Billing provider rejected customer '{customerReference}': {message}", ex);
        }
    }

    public async Task<Subscription?> FindSubscriptionByCustomerReferenceAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await TryReadCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer?.Id is not int customerId)
        {
            return null;
        }

        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Failed to list subscriptions for customer '{customerReference}': {DescribeRaw(ex.Error)}", ex);
        }

        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!, customerReference))
            .OrderByDescending(s => s.IsActive)
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    public async Task<Subscription> CreateSubscriptionAsync(string customerReference, string productHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerReference = customerReference,
                },
            }, cancellationToken);

            return MapSubscription(response.Subscription!, customerReference);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var message = ex.Error.TryGetErrorListResponse1(out var validation)
                ? string.Join("; ", validation.Errors)
                : DescribeRawFallback(ex.Error);
            throw new BillingProviderException($"Billing provider rejected enrollment in '{productHandle}': {message}", ex);
        }
    }

    public async Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: cancellationToken);
            if (response.Subscription is null)
            {
                throw new SubscriptionNotFoundException(subscriptionId);
            }

            return MapSubscription(response.Subscription, response.Subscription.Customer?.Reference ?? string.Empty);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Failed to read subscription {subscriptionId}: {DescribeRaw(ex.Error)}", ex);
        }
    }

    public Task<bool> IsMeteredComponentConfiguredCorrectlyAsync(CancellationToken cancellationToken = default)
        => IsMeteredComponentValidAsync(cancellationToken);

    public async Task<UsageRecord> RecordUsageAsync(int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default)
    {
        if (!await IsMeteredComponentValidAsync(cancellationToken))
        {
            throw new MeteredComponentMisconfiguredException(_settings.MeteredComponentHandle);
        }

        try
        {
            var response = await _client.SubscriptionComponents.CreateUsage(
                subscriptionId,
                _settings.MeteredComponentId,
                new CreateUsageRequest { Usage = new CreateUsage { Quantity = quantity, Memo = memo } },
                cancellationToken);

            var usage = response.Usage;
            return new UsageRecord(usage.Id ?? 0, usage.ComponentHandle ?? _settings.MeteredComponentHandle, quantity, memo, usage.CreatedAt);
        }
        catch (SdkException<CreateUsageError> ex)
        {
            var message = ex.Error.TryGetErrorListResponse1(out var validation)
                ? string.Join("; ", validation.Errors)
                : DescribeRawFallback(ex.Error);
            throw new BillingProviderException($"Billing provider rejected the usage record: {message}", ex);
        }
    }

    public async Task<UsagePeriodSummary> GetUsagePeriodToDateAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        if (!await IsMeteredComponentValidAsync(cancellationToken))
        {
            throw new MeteredComponentMisconfiguredException(_settings.MeteredComponentHandle);
        }

        try
        {
            var usages = await _client.SubscriptionComponents.ListUsages(
                subscriptionId,
                _settings.MeteredComponentId,
                sinceId: null,
                maxId: null,
                sinceDate: null,
                untilDate: null,
                page: 1,
                perPage: 200,
                ct: cancellationToken);

            var total = usages.Sum(u => ReadQuantity(u.Usage.Quantity));
            return new UsagePeriodSummary(_settings.MeteredComponentHandle, total, available: true);
        }
        catch (Exception)
        {
            // The usage record already succeeded - a failed read-back must not fail the whole operation
            // (UC2 failure scenario): report success with the total marked unavailable.
            return new UsagePeriodSummary(_settings.MeteredComponentHandle, null, available: false);
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool immediate, CancellationToken cancellationToken = default)
    {
        var current = await GetSubscriptionAsync(subscriptionId, cancellationToken);

        if (!immediate)
        {
            // "At renewal" changes defer to the next period with no proration (UpdateSubscription's
            // ProductChangeDelayed), so the previewed cost is simply the target plan's flat price - the
            // migrations-preview endpoint below only models the immediate, prorated path.
            var targetProduct = await ReadProductByHandleAsync(targetProductHandle, cancellationToken);
            return new PlanChangePreview(
                subscriptionId,
                current.ProductHandle,
                targetProductHandle,
                immediate: false,
                proratedAdjustmentInCents: 0,
                chargeInCents: 0,
                paymentDueInCents: targetProduct.PriceInCents ?? 0,
                creditAppliedInCents: 0,
                commitToken: string.Empty);
        }

        try
        {
            var response = await _client.SubscriptionProducts.PreviewSubscriptionProductMigration(
                subscriptionId,
                new SubscriptionMigrationPreviewRequest
                {
                    Migration = new SubscriptionMigrationPreviewOptions
                    {
                        ProductHandle = targetProductHandle,
                    },
                },
                cancellationToken);

            var migration = response.Migration;
            return new PlanChangePreview(
                subscriptionId,
                current.ProductHandle,
                targetProductHandle,
                immediate: true,
                migration.ProratedAdjustmentInCents,
                migration.ChargeInCents,
                migration.PaymentDueInCents,
                migration.CreditAppliedInCents,
                commitToken: string.Empty);
        }
        catch (SdkException<PreviewSubscriptionProductMigrationError> ex)
        {
            var message = ex.Error.TryGetErrorListResponse1(out var validation)
                ? string.Join("; ", validation.Errors)
                : DescribeRawFallback(ex.Error);
            throw new BillingProviderException($"Failed to preview plan change to '{targetProductHandle}': {message}", ex);
        }
    }

    public async Task<Subscription> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, bool immediate, CancellationToken cancellationToken = default)
    {
        if (!immediate)
        {
            try
            {
                var response = await _client.Subscriptions.UpdateSubscription(
                    subscriptionId,
                    new UpdateSubscriptionRequest
                    {
                        Subscription = new UpdateSubscription
                        {
                            ProductHandle = targetProductHandle,
                            ProductChangeDelayed = true,
                        },
                    },
                    cancellationToken);

                return MapSubscription(response.Subscription!, response.Subscription?.Customer?.Reference ?? string.Empty);
            }
            catch (SdkException<UpdateSubscriptionError> ex)
            {
                var message = ex.Error.TryGetErrorListResponse1(out var validation)
                    ? string.Join("; ", validation.Errors)
                    : DescribeRawFallback(ex.Error);
                throw new BillingProviderException($"Failed to schedule plan change to '{targetProductHandle}': {message}", ex);
            }
        }

        try
        {
            var response = await _client.SubscriptionProducts.MigrateSubscriptionProduct(
                subscriptionId,
                new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration
                    {
                        ProductHandle = targetProductHandle,
                    },
                },
                cancellationToken);

            return MapSubscription(response.Subscription!, response.Subscription?.Customer?.Reference ?? string.Empty);
        }
        catch (SdkException<MigrateSubscriptionProductError> ex)
        {
            var message = ex.Error.TryGetErrorListResponse1(out var validation)
                ? string.Join("; ", validation.Errors)
                : DescribeRawFallback(ex.Error);
            throw new BillingProviderException($"Failed to change plan to '{targetProductHandle}': {message}", ex);
        }
    }

    public async Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.PauseSubscription(subscriptionId, body: null, ct: cancellationToken);
            return MapSubscription(response.Subscription!, response.Subscription?.Customer?.Reference ?? string.Empty);
        }
        catch (SdkException<PauseSubscriptionError> ex)
        {
            var message = ex.Error.TryGetErrorListResponse1(out var validation)
                ? string.Join("; ", validation.Errors)
                : DescribeRawFallback(ex.Error);
            throw new BillingProviderException($"Failed to pause subscription {subscriptionId}: {message}", ex);
        }
    }

    public async Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ResumeSubscription(subscriptionId, calendarBillingResumptionCharge: null, ct: cancellationToken);
            return MapSubscription(response.Subscription!, response.Subscription?.Customer?.Reference ?? string.Empty);
        }
        catch (SdkException<ResumeSubscriptionError> ex)
        {
            var message = ex.Error.TryGetErrorListResponse1(out var validation)
                ? string.Join("; ", validation.Errors)
                : DescribeRawFallback(ex.Error);
            throw new BillingProviderException($"Failed to resume subscription {subscriptionId}: {message}", ex);
        }
    }

    public async Task<Subscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.CancelSubscription(
                subscriptionId,
                new CancellationRequest
                {
                    Subscription = new CancellationOptions
                    {
                        CancellationMessage = reason,
                        CancelAtEndOfPeriod = endOfPeriod,
                    },
                },
                cancellationToken);

            return MapSubscription(response.Subscription!, response.Subscription?.Customer?.Reference ?? string.Empty);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            string message;
            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var validation))
            {
                message = validation.TryGetErrorListResponse1(out var errorList)
                    ? string.Join("; ", errorList.Errors)
                    : validation.TryGetSingleErrorResponse1(out var single)
                        ? single.Error
                        : validation.ToString();
            }
            else
            {
                message = DescribeRawFallback(ex.Error);
            }

            throw new BillingProviderException($"Failed to cancel subscription {subscriptionId}: {message}", ex);
        }
    }

    public async Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.SubscriptionStatus.ReactivateSubscription(subscriptionId, body: null, ct: cancellationToken);
            return MapSubscription(response.Subscription!, response.Subscription?.Customer?.Reference ?? string.Empty);
        }
        catch (SdkException<ReactivateSubscriptionError> ex)
        {
            var message = ex.Error.TryGetErrorListResponse1(out var validation)
                ? string.Join("; ", validation.Errors)
                : DescribeRawFallback(ex.Error);
            throw new BillingProviderException($"Failed to reactivate subscription {subscriptionId}: {message}", ex);
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(customerReference, cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Failed to look up billing customer '{customerReference}': {DescribeRaw(ex.Error)}", ex);
        }
    }

    private async Task<Product> ReadProductByHandleAsync(string productHandle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Products.ReadProductByHandle(productHandle, cancellationToken);
            return response.Product;
        }
        catch (SdkException<RawError> ex)
        {
            throw new BillingProviderException($"Failed to read plan '{productHandle}': {DescribeRaw(ex.Error)}", ex);
        }
    }

    private async Task<bool> IsMeteredComponentValidAsync(CancellationToken cancellationToken)
    {
        var handle = _settings.MeteredComponentHandle;
        if (MeteredComponentValidated.TryGetValue(handle, out var cached))
        {
            return cached;
        }

        bool isValid;
        try
        {
            var response = await _client.Components.FindComponent(handle, cancellationToken);
            isValid = response.Component?.Kind == ComponentKind.MeteredComponent;
        }
        catch (SdkException<RawError>)
        {
            isValid = false;
        }

        MeteredComponentValidated[handle] = isValid;
        return isValid;
    }

    private static BillingCustomer MapCustomer(Customer customer)
        => new(customer.Id ?? 0, customer.Reference ?? string.Empty, customer.Email ?? string.Empty);

    private static Subscription MapSubscription(MaxioSubscription subscription, string fallbackCustomerReference)
        => new(
            subscription.Id ?? 0,
            subscription.Customer?.Id ?? 0,
            subscription.Customer?.Reference ?? fallbackCustomerReference,
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
            subscription.State?.Value ?? "unknown",
            subscription.CurrentPeriodStartedAt,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.CancelAtEndOfPeriod ?? false,
            subscription.ScheduledCancellationAt,
            subscription.ActivatedAt,
            subscription.CreatedAt);

    private static double ReadQuantity(Quantity1? quantity)
    {
        if (quantity is null)
        {
            return 0d;
        }

        if (quantity.TryGetInt(out var intValue))
        {
            return intValue;
        }

        if (quantity.TryGetString(out var stringValue) && double.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0d;
    }

    private static string DescribeRaw(RawError raw) => $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}";

    private static string DescribeRawFallback<TError>(TError error) where TError : ApiError
        => error.TryGetRawError(out var raw) ? DescribeRaw(raw) : "unknown error";
}
