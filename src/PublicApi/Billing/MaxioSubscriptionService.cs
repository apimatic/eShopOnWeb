using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionService"/>.
///
/// Idempotency: the billing customer is keyed on a stable reference derived from
/// the eShopOnWeb user id (<c>eshop-user-{userId}</c>) — Maxio enforces customer
/// reference uniqueness, so a lookup-first create can never produce two customers.
/// A subscription is keyed on <c>eshop-sub-{userId}-{planHandle}</c>; the create is
/// gated on a not-found lookup by that reference, and any ambiguous outcome
/// (transport fault, refused re-send, unreadable response) is reconciled by
/// re-reading the provider by reference.
///
/// Plans are never hard-coded: they are read live from the product family named by
/// <see cref="MaxioSettings.ProductFamilyHandle"/>, so the same build runs against
/// any Maxio site and catalog.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const int PageSize = 100;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        if (familyId is null)
        {
            return Array.Empty<SubscriptionPlan>();
        }

        var plans = new List<SubscriptionPlan>();
        var page = 1;
        while (true)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await _client.ProductFamilies.ListProductsForProductFamily(
                    familyId.Value.ToString(CultureInfo.InvariantCulture),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: page,
                    perPage: PageSize,
                    ct: cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex) when (ex.Error.TryGetString(out _))
            {
                // The family vanished between the two lookups; report no plans rather than fail.
                return plans;
            }
            catch (SdkException<ListProductsForProductFamilyError> ex) when (ex.Error.TryGetRawError(out var raw))
            {
                throw WrapRaw("Listing the plans of the product family", raw.StatusCode, raw);
            }
            catch (Exception ex) when (IsUnreadableOrUnreachable(ex, cancellationToken))
            {
                throw WrapUnreadableOrUnreachable(ex, "Listing the plans of the product family");
            }

            foreach (var response in products)
            {
                var product = response.Product;
                if (product?.Handle is null || product.ArchivedAt is not null)
                {
                    continue;
                }
                plans.Add(new SubscriptionPlan(
                    product.Handle,
                    product.Name ?? product.Handle,
                    product.PriceInCents ?? 0,
                    product.Interval ?? 1,
                    product.IntervalUnit?.Value ?? "month"));
            }

            if (products.Count < PageSize)
            {
                break;
            }
            page++;
        }
        return plans;
    }

    public async Task<SubscriptionSummary> SubscribeAsync(SubscriberProfile profile, string planHandle, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planHandle);

        var product = await ReadPlanOrThrowAsync(planHandle, cancellationToken);
        EnsurePlanInConfiguredFamily(product);

        var customerReference = CustomerReference(profile);
        var customer = await EnsureCustomerAsync(profile, customerReference, cancellationToken);

        var subscriptionReference = SubscriptionReference(profile, planHandle);
        var existing = await FindSubscriptionOrNullAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Idempotent subscribe replay for user {UserId}: subscription {SubscriptionId} on plan {PlanHandle} already exists.",
                profile.UserId, existing.Id, planHandle);
            return MapSubscription(existing);
        }

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customer.Id,
                Reference = subscriptionReference,
                // Payment-less signup: the plans require no card capture, but omitting every
                // payment field alone is rejected ("No payment method was on file for the
                // $… balance"). Remittance is the Relationship-Invoicing-valid manual-payment
                // collection method (Invoice is legacy-Statements-only and must not be sent).
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        SubscriptionResponse created;
        try
        {
            // The scope covers exactly the one SDK write call: any re-send the SDK's
            // transport-retry pipeline attempts inside it is refused, and the
            // reconciliation lookups below run once the scope is closed.
            using (SingleSendHandler.BeginSingleSendScope())
            {
                created = await _client.Subscriptions.CreateSubscription(body, cancellationToken);
            }
        }
        catch (SdkException<CreateSubscriptionError> ex) when (ex.Error.TryGetErrorListResponse1(out var validation))
        {
            throw new MaxioBillingException(
                422,
                "The billing provider rejected the subscription request.",
                validation.Errors ?? Array.Empty<string>());
        }
        catch (SdkException<CreateSubscriptionError> ex) when (ex.Error.TryGetRawError(out var raw))
        {
            throw WrapRaw("Creating the subscription", raw.StatusCode, raw);
        }
        catch (Exception ex) when (ex is DuplicateSendException || IsUnreadableOrUnreachable(ex, cancellationToken))
        {
            // The single allowed send may or may not have taken effect — settle it by reference.
            return await ReconcileOrCreateAsync(
                subscriptionReference,
                "The subscription request could not be confirmed; no duplicate has been created and it is safe to retry.",
                cancellationToken);
        }

        _logger.LogInformation(
            "User {UserId} subscribed to plan {PlanHandle}: subscription {SubscriptionId}.",
            profile.UserId, planHandle, created.Subscription?.Id);
        return MapSubscription(created);
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(SubscriberProfile profile, CancellationToken cancellationToken)
    {
        var customer = await ReadCustomerOrNullAsync(CustomerReference(profile), cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRaw("Listing the customer's subscriptions", ex.Error.StatusCode, ex.Error);
        }
        catch (Exception ex) when (IsUnreadableOrUnreachable(ex, cancellationToken))
        {
            throw WrapUnreadableOrUnreachable(ex, "Listing the customer's subscriptions");
        }

        return subscriptions.Select(s => MapSubscription(s)).ToList();
    }

    private async Task<int?> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
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
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("No product families exist at the billing provider; configured family '{FamilyHandle}' is unavailable.",
                _settings.ProductFamilyHandle);
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRaw("Listing product families", ex.Error.StatusCode, ex.Error);
        }
        catch (Exception ex) when (IsUnreadableOrUnreachable(ex, cancellationToken))
        {
            throw WrapUnreadableOrUnreachable(ex, "Listing product families");
        }

        return families
            .FirstOrDefault(f => string.Equals(f.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            ?.ProductFamily?.Id;
    }

    private async Task<Product> ReadPlanOrThrowAsync(string planHandle, CancellationToken cancellationToken)
    {
        ProductResponse response;
        try
        {
            response = await _client.Products.ReadProductByHandle(planHandle, ct: cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioBillingException(404, $"Unknown subscription plan '{planHandle}'.");
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRaw($"Reading plan '{planHandle}'", ex.Error.StatusCode, ex.Error);
        }
        catch (Exception ex) when (IsUnreadableOrUnreachable(ex, cancellationToken))
        {
            throw WrapUnreadableOrUnreachable(ex, $"Reading plan '{planHandle}'");
        }

        return response.Product
            ?? throw new MaxioBillingException(502, "The billing provider returned an empty plan payload.");
    }

    private void EnsurePlanInConfiguredFamily(Product product)
    {
        if (!string.Equals(product.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new MaxioBillingException(
                404,
                $"Plan '{product.Handle}' is not part of the configured subscription catalog.");
        }
    }

    private async Task<Customer?> ReadCustomerOrNullAsync(string customerReference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(customerReference, ct: cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw WrapRaw($"Looking up customer '{customerReference}'", ex.Error.StatusCode, ex.Error);
        }
        catch (Exception ex) when (IsUnreadableOrUnreachable(ex, cancellationToken))
        {
            throw WrapUnreadableOrUnreachable(ex, $"Looking up customer '{customerReference}'");
        }
    }

    private async Task<Customer> EnsureCustomerAsync(SubscriberProfile profile, string customerReference, CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerOrNullAsync(customerReference, cancellationToken);
        if (existing?.Id is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = profile.FirstName,
                LastName = profile.LastName,
                Email = profile.Email,
                Reference = customerReference
            }
        };

        try
        {
            CustomerResponse created;
            using (SingleSendHandler.BeginSingleSendScope())
            {
                created = await _client.Customers.CreateCustomer(body, cancellationToken);
            }
            return created.Customer
                ?? throw new MaxioBillingException(502, "The billing provider returned an empty customer payload.");
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            // Either a validation rejection or a duplicate-reference race lost against a
            // concurrent create — settle it by re-reading the reference.
            var raced = await ReadCustomerOrNullAsync(customerReference, cancellationToken);
            if (raced?.Id is not null)
            {
                return raced;
            }
            throw new MaxioBillingException(422, "The billing provider rejected the customer request.");
        }
        catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetRawError(out var raw))
        {
            throw WrapRaw("Creating the customer", raw.StatusCode, raw);
        }
        catch (Exception ex) when (ex is DuplicateSendException || IsUnreadableOrUnreachable(ex, cancellationToken))
        {
            var reconciled = await ReadCustomerOrNullAsync(customerReference, cancellationToken);
            if (reconciled?.Id is not null)
            {
                return reconciled;
            }
            throw new MaxioBillingException(
                503,
                "The customer request could not be confirmed; no duplicate has been created and it is safe to retry.");
        }
    }

    private async Task<SubscriptionSummary> ReconcileOrCreateAsync(string subscriptionReference, string unresolvedMessage, CancellationToken cancellationToken)
    {
        var reconciled = await FindSubscriptionOrNullAsync(subscriptionReference, cancellationToken);
        if (reconciled is not null)
        {
            return MapSubscription(reconciled);
        }
        throw new MaxioBillingException(503, unresolvedMessage);
    }

    private async Task<Subscription?> FindSubscriptionOrNullAsync(string subscriptionReference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(subscriptionReference, ct: cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            // 404: no subscription carries this reference yet.
            return null;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetRawError(out var raw))
        {
            throw WrapRaw($"Finding subscription '{subscriptionReference}'", raw.StatusCode, raw);
        }
        catch (Exception ex) when (IsUnreadableOrUnreachable(ex, cancellationToken))
        {
            throw WrapUnreadableOrUnreachable(ex, $"Finding subscription '{subscriptionReference}'");
        }
    }

    private static SubscriptionSummary MapSubscription(SubscriptionResponse response)
    {
        var subscription = response.Subscription
            ?? throw new MaxioBillingException(502, "The billing provider returned an empty subscription payload.");
        return MapSubscription(subscription);
    }

    private static SubscriptionSummary MapSubscription(Subscription subscription)
    {
        return new SubscriptionSummary(
            subscription.Id ?? 0,
            subscription.Reference ?? string.Empty,
            subscription.State?.Value ?? "unknown",
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt,
            subscription.Customer?.Id ?? 0,
            subscription.Customer?.Reference ?? string.Empty);
    }

    private static string CustomerReference(SubscriberProfile profile) => $"eshop-user-{profile.UserId}";

    private static string SubscriptionReference(SubscriberProfile profile, string planHandle) =>
        $"eshop-sub-{profile.UserId}-{planHandle}";

    private static bool IsUnreadableOrUnreachable(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException
        || ex is System.Text.Json.JsonException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private MaxioBillingException WrapRaw(string operation, HttpStatusCode statusCode, RawError error)
    {
        var status = (int)statusCode;
        _logger.LogWarning("Maxio call failed: {Operation} -> HTTP {Status} {Detail}",
            operation, status, Truncate(SafeBody(error)));
        var mapped = status is >= 400 and <= 499 ? status : 502;
        return new MaxioBillingException(mapped, $"{operation} failed at the billing provider (HTTP {status}).");
    }

    private MaxioBillingException WrapUnreadableOrUnreachable(Exception ex, string operation)
    {
        _logger.LogWarning(ex, "Maxio call did not complete cleanly: {Operation}", operation);
        return new MaxioBillingException(
            502,
            $"{operation} could not be completed against the billing provider.");
    }

    private static string SafeBody(RawError error)
    {
        try
        {
            return error.ReadAsString() ?? string.Empty;
        }
        catch
        {
            return "<unreadable error body>";
        }
    }

    private static string Truncate(string text) =>
        text.Length <= 500 ? text : text[..500];
}