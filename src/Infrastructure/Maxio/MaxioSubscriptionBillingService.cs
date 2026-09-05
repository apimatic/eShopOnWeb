using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.Extensions.Options;
using CoreSubscription = Microsoft.eShopWeb.ApplicationCore.Subscriptions.Subscription;
using CoreSubscriptionPlan = Microsoft.eShopWeb.ApplicationCore.Subscriptions.SubscriptionPlan;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Fronts Maxio Advanced Billing. Resolves the product family by handle (numeric ids are not
/// stable across a re-seed), and makes <see cref="SubscribeAsync"/> idempotent per buyer by
/// keying the Maxio customer's "reference" field on the eShopOnWeb buyer id (their username).
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionBillingService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<CoreSubscriptionPlan>> GetAvailablePlansAsync(CancellationToken ct = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(ct);

        IReadOnlyList<ProductResponse> products;
        try
        {
            products = await GuardTransportAsync(() => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 20,
                ct: ct));
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFoundMessage))
            {
                throw new SubscriptionBillingException(HttpStatusCode.NotFound,
                    notFoundMessage ?? "Product family not found.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToBillingException(raw, "Unable to list subscription plans.");
            }
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway, "Unexpected error from billing provider.", ex);
        }

        return products
            .Select(p => p.Product)
            .Where(p => p is not null)
            .Select(p => new CoreSubscriptionPlan
            {
                Handle = p!.Handle ?? string.Empty,
                Name = p.Name ?? string.Empty,
                Price = ToPrice(p.PriceInCents),
                Interval = p.Interval ?? 0,
                IntervalUnit = p.IntervalUnit?.Value ?? string.Empty
            })
            .ToList();
    }

    public async Task<CoreSubscription> SubscribeAsync(string buyerId, string planHandle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw new ArgumentException("Buyer id is required.", nameof(buyerId));
        if (string.IsNullOrWhiteSpace(planHandle)) throw new ArgumentException("Plan handle is required.", nameof(planHandle));

        var customerId = await ResolveOrCreateCustomerIdAsync(buyerId, ct);
        var collectionMethod = await ResolveNonAutomaticCollectionMethodIfNeededAsync(ct);

        SubscriptionResponse response;
        try
        {
            response = await GuardTransportAsync(() => _client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerId = customerId,
                        PaymentCollectionMethod = collectionMethod
                    }
                },
                ct: ct));
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var message = errorList.Errors is { Count: > 0 }
                    ? string.Join(" ", errorList.Errors)
                    : "The billing provider rejected the subscription request.";
                throw new SubscriptionBillingException(HttpStatusCode.UnprocessableEntity, message, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToBillingException(raw, "Unable to create the subscription.");
            }
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway, "Unexpected error from billing provider.", ex);
        }

        var subscription = response.Subscription
            ?? throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The billing provider did not return the created subscription.");

        return MapSubscription(subscription);
    }

    public async Task<IReadOnlyList<CoreSubscription>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw new ArgumentException("Buyer id is required.", nameof(buyerId));

        var customerId = await TryReadCustomerIdByReferenceAsync(buyerId, ct);
        if (!customerId.HasValue)
        {
            return Array.Empty<CoreSubscription>();
        }

        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await GuardTransportAsync(() =>
                _client.Customers.ListCustomerSubscriptions(customerId: customerId.Value, ct: ct));
        }
        catch (SdkException<RawError> ex)
        {
            throw ToBillingException(ex.Error, "Unable to list subscriptions.");
        }

        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await GuardTransportAsync(() => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct));
        }
        catch (SdkException<RawError> ex)
        {
            throw ToBillingException(ex.Error, "Unable to list product families.");
        }

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null &&
                string.Equals(f.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is null)
        {
            throw new SubscriptionBillingException(HttpStatusCode.NotFound,
                $"Product family '{_options.ProductFamilyHandle}' was not found in Maxio.");
        }

        return family.Id.Value;
    }

    /// <summary>
    /// These plans have no trial, so a subscription's first charge is assessed immediately at creation.
    /// A site whose default payment-collection method is "automatic" auto-charges that balance and
    /// therefore rejects creation when the customer has no payment profile on file - verified live
    /// against this integration's sandbox (a 422 "No payment method was on file"). Sites are split into
    /// two billing architectures with disjoint valid values (source: MaxioAdvancedBilling.Models.Enums.
    /// CollectionMethod's doc comment): Relationship Invoicing sites accept remittance/automatic/prepaid;
    /// legacy sites accept invoice/automatic. "prepaid" is rejected for paid products (also verified
    /// live), so remittance/invoice are the only viable non-automatic choices, picked by architecture.
    /// When the site's own default is already something other than "automatic", this returns null so
    /// the subscription inherits it rather than overriding a deliberate site configuration.
    /// </summary>
    private async Task<CollectionMethod?> ResolveNonAutomaticCollectionMethodIfNeededAsync(CancellationToken ct)
    {
        SiteResponse site;
        try
        {
            site = await GuardTransportAsync(() => _client.Sites.ReadSite(ct: ct));
        }
        catch (SdkException<RawError> ex)
        {
            throw ToBillingException(ex.Error, "Unable to read the billing site configuration.");
        }

        if (!string.Equals(site.Site?.DefaultPaymentCollectionMethod, "automatic", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return site.Site?.RelationshipInvoicingEnabled == true
            ? CollectionMethod.Remittance
            : CollectionMethod.Invoice;
    }

    private async Task<int> ResolveOrCreateCustomerIdAsync(string buyerId, CancellationToken ct)
    {
        var existingId = await TryReadCustomerIdByReferenceAsync(buyerId, ct);
        if (existingId.HasValue)
        {
            return existingId.Value;
        }

        CustomerResponse created;
        try
        {
            created = await GuardTransportAsync(() => _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = DeriveFirstName(buyerId),
                        LastName = "eShopOnWeb Customer",
                        Email = buyerId,
                        Reference = buyerId
                    }
                },
                ct: ct));
        }
        catch (SdkException<CreateCustomerError>)
        {
            // CreateCustomer's only documented validation failure is a duplicate reference, so a 422
            // immediately after a failed lookup is the expected shape of a race (e.g. a double-click)
            // where a concurrent request created this reference first. Re-look-up recovers it instead
            // of trusting the 422 payload's shape - see maxio-plan.md's trap note: the generated 422
            // model does not actually carry a reference-duplicate field.
            var recovered = await TryReadCustomerIdByReferenceAsync(buyerId, ct);
            if (recovered.HasValue)
            {
                return recovered.Value;
            }

            throw new SubscriptionBillingException(HttpStatusCode.UnprocessableEntity,
                "The billing provider rejected the customer creation request.");
        }

        return created.Customer?.Id
            ?? throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The billing provider did not return a customer id.");
    }

    private async Task<int?> TryReadCustomerIdByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await GuardTransportAsync(() =>
                _client.Customers.ReadCustomerByReference(reference: reference, ct: ct));
            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ToBillingException(ex.Error, "Unable to look up the billing customer.");
        }
    }

    private static CoreSubscription MapSubscription(Subscription subscription)
    {
        return new CoreSubscription
        {
            Id = subscription.Id ?? 0,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            Price = ToPrice(subscription.ProductPriceInCents),
            State = subscription.State?.Value ?? string.Empty,
            NextBillingDate = subscription.CurrentPeriodEndsAt
        };
    }

    private static decimal ToPrice(long? priceInCents) => priceInCents.HasValue ? priceInCents.Value / 100m : 0m;

    private static string DeriveFirstName(string buyerId)
    {
        var atIndex = buyerId.IndexOf('@');
        var candidate = atIndex > 0 ? buyerId[..atIndex] : buyerId;
        return string.IsNullOrWhiteSpace(candidate) ? "eShopOnWeb" : candidate;
    }

    private static SubscriptionBillingException ToBillingException(RawError raw, string fallbackMessage)
    {
        var body = raw.ReadAsString();
        var message = string.IsNullOrWhiteSpace(body) ? fallbackMessage : body;
        return new SubscriptionBillingException(MapStatus(raw.StatusCode), message);
    }

    private static HttpStatusCode MapStatus(HttpStatusCode providerStatus) =>
        (int)providerStatus is >= 400 and < 500 ? providerStatus : HttpStatusCode.BadGateway;

    /// <summary>
    /// Converts a broken 2xx body (JsonException) or a transport failure into a SubscriptionBillingException.
    /// Left to propagate unhandled, SdkException&lt;TError&gt; from the wrapped call - it is caught by each
    /// call site's own catch, which knows the operation's concrete error type.
    /// </summary>
    private static async Task<T> GuardTransportAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (JsonException ex)
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SubscriptionBillingException(HttpStatusCode.ServiceUnavailable,
                "The billing provider is currently unreachable.", ex);
        }
    }
}
