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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Canceled/expired/failed-to-create subscriptions don't block re-subscribing to the same plan.
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly string _productFamilyHandle;

    // The site's billing architecture (legacy Statements vs current Relationship Invoicing) decides which
    // PaymentCollectionMethod is legal - it doesn't change at runtime, so cache it for this service's
    // lifetime rather than re-fetching on every subscribe. Register this service as a singleton so the
    // cache actually spans requests. See maxio-plan.md §5.
    private CollectionMethod? _cachedPaymentCollectionMethod;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings)
    {
        _client = client;
        _productFamilyHandle = settings.Value.ProductFamilyHandle;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default)
    {
        var productFamilyId = "handle:" + _productFamilyHandle;
        var plans = new List<SubscriptionPlan>();
        var page = 1;
        const int perPage = 20;

        while (true)
        {
            IReadOnlyList<ProductResponse> pageResponse;
            try
            {
                pageResponse = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: productFamilyId,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: perPage,
                    ct: ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new MaxioSubscriptionException(HttpStatusCode.InternalServerError,
                        "Subscription plans are temporarily unavailable.", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                    throw TranslateRawError(raw, ex);

                throw new MaxioSubscriptionException(HttpStatusCode.InternalServerError,
                    "Subscription plans are temporarily unavailable.", ex);
            }
            catch (JsonException ex)
            {
                throw new MaxioSubscriptionException(HttpStatusCode.BadGateway,
                    "The billing provider returned a response that could not be processed.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new MaxioSubscriptionException(HttpStatusCode.BadGateway, "The billing provider is unreachable.", ex);
            }

            foreach (var item in pageResponse)
            {
                if (item.Product is { } product)
                    plans.Add(MapPlan(product));
            }

            if (pageResponse.Count < perPage)
                break;
            page++;
        }

        return plans;
    }

    public async Task<UserSubscription> SubscribeAsync(string userReference, string email, string planHandle, CancellationToken ct = default)
    {
        var customerId = await EnsureCustomerAsync(userReference, email, ct);

        var existingSubscriptions = await FetchSubscriptionsAsync(customerId, ct);
        var activeForPlan = existingSubscriptions
            .Select(s => s.Subscription)
            .FirstOrDefault(s =>
                s is not null &&
                string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                !IsTerminal(s.State?.Value));

        // A repeat subscribe (double-click, or subscribing again after already being enrolled) returns
        // the existing enrollment instead of creating a second one - see maxio-plan.md §5 (YOUR CALL).
        if (activeForPlan is not null)
            return MapSubscription(activeForPlan);

        // A plan with RequireCreditCard:false still charges the full price immediately (no trial) under
        // the default Automatic collection method, which is rejected outright with no payment profile on
        // file. Route it through the site's non-card collection method instead. See maxio-plan.md §5.
        var paymentCollectionMethod = await ResolvePaymentCollectionMethodAsync(ct);

        SubscriptionResponse created;
        try
        {
            created = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = paymentCollectionMethod,
                    NetTerms = "0"
                }
            }, ct: ct);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
                throw new MaxioSubscriptionException(HttpStatusCode.BadRequest, string.Join(" ", errorList.Errors), ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw TranslateRawError(raw, ex);

            throw new MaxioSubscriptionException(HttpStatusCode.BadRequest, "The subscription request was rejected.", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioSubscriptionException(HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioSubscriptionException(HttpStatusCode.BadGateway, "The billing provider is unreachable.", ex);
        }

        if (created.Subscription is not { } subscription)
            throw new MaxioSubscriptionException(HttpStatusCode.BadGateway, "The billing provider did not return a subscription.");

        return MapSubscription(subscription);
    }

    public async Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        var customerId = await TryReadCustomerByReferenceAsync(userReference, ct);
        if (customerId is null)
            return Array.Empty<UserSubscription>();

        var subscriptions = await FetchSubscriptionsAsync(customerId.Value, ct);
        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    private async Task<int> EnsureCustomerAsync(string reference, string email, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(reference, ct);
        if (existing is { } existingId)
            return existingId;

        var (firstName, lastName) = DeriveName(email);

        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            }, ct: ct);

            if (created.Customer?.Id is { } newId)
                return newId;

            throw new MaxioSubscriptionException(HttpStatusCode.BadGateway,
                "The billing provider did not return a customer id.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // The operation's only documented validation restriction is reference-uniqueness, so any 422
            // here is treated as a possible duplicate-reference race - recover by re-reading rather than
            // failing a legitimate double-click. See maxio-plan.md §5.
            var recovered = await TryReadCustomerByReferenceAsync(reference, ct);
            if (recovered is { } recoveredId)
                return recoveredId;

            throw new MaxioSubscriptionException(HttpStatusCode.BadRequest, DescribeCreateCustomerError(ex.Error), ex);
        }
        catch (JsonException ex)
        {
            // A malformed body leaves the true outcome unknown - reconcile by re-reading rather than
            // guessing whether the create actually took effect. See dotnet-error-handling.
            var recovered = await TryReadCustomerByReferenceAsync(reference, ct);
            if (recovered is { } recoveredId)
                return recoveredId;

            throw new MaxioSubscriptionException(HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            var recovered = await TryReadCustomerByReferenceAsync(reference, ct);
            if (recovered is { } recoveredId)
                return recoveredId;

            throw new MaxioSubscriptionException(HttpStatusCode.BadGateway, "The billing provider is unreachable.", ex);
        }
    }

    private async Task<CollectionMethod> ResolvePaymentCollectionMethodAsync(CancellationToken ct)
    {
        if (_cachedPaymentCollectionMethod is { } cached)
            return cached;

        SiteResponse site;
        try
        {
            site = await _client.Sites.ReadSite(ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioSubscriptionException(HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioSubscriptionException(HttpStatusCode.BadGateway, "The billing provider is unreachable.", ex);
        }

        var method = site.Site?.RelationshipInvoicingEnabled == true
            ? CollectionMethod.Remittance
            : CollectionMethod.Invoice;
        _cachedPaymentCollectionMethod = method;
        return method;
    }

    private async Task<int?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer?.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioSubscriptionException(HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioSubscriptionException(HttpStatusCode.BadGateway, "The billing provider is unreachable.", ex);
        }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> FetchSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioSubscriptionException(HttpStatusCode.BadGateway,
                "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioSubscriptionException(HttpStatusCode.BadGateway, "The billing provider is unreachable.", ex);
        }
    }

    private static string DescribeCreateCustomerError(CreateCustomerError error)
    {
        if (error.TryGetRawError(out var raw))
            return SafeReadRawError(raw);

        // A 422 only ever populates TryGetCustomerErrorResponse1, whose payload (PerPage/PricePoint)
        // carries nothing customer-related and has no raw-body fallback for that status - confirmed
        // against the SDK source in maxio-plan.md §5.
        return "The request to create a billing customer was rejected.";
    }

    private static MaxioSubscriptionException TranslateRawError(RawError raw, Exception inner)
    {
        var status = raw.StatusCode;
        var mapped = status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
            ? status
            : HttpStatusCode.BadGateway;
        return new MaxioSubscriptionException(mapped, SafeReadRawError(raw), inner);
    }

    private static string SafeReadRawError(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return string.IsNullOrWhiteSpace(body) ? "The billing provider rejected the request." : body;
        }
        catch
        {
            return "The billing provider rejected the request.";
        }
    }

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        PriceInCents = product.PriceInCents ?? 0,
        IntervalCount = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
        HasTrial = product.TrialPriceInCents is > 0 || product.TrialInterval is > 0,
        TrialPriceInCents = product.TrialPriceInCents,
        TrialIntervalCount = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit?.Value,
        SetupFeeInCents = product.InitialChargeInCents,
        RequiresPaymentMethod = product.RequireCreditCard ?? false,
        Taxable = product.Taxable ?? false,
        ExpiresNever = product.ExpirationIntervalUnit?.Value == "never"
            || (product.ExpirationInterval is null && product.ExpirationIntervalUnit is null)
    };

    private static UserSubscription MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        State = subscription.State?.Value ?? string.Empty,
        NextBillingDate = subscription.NextAssessmentAt
    };

    private static bool IsTerminal(string? state) => state is not null && TerminalStates.Contains(state);

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        // ApplicationUser (ASP.NET Identity) carries no real name, but CreateCustomer requires
        // FirstName/LastName - derive a reasonable display name from the email local-part.
        // See maxio-plan.md §5 (blocker).
        var localPart = email.Split('@')[0];
        var segments = localPart.Split(new[] { '.', '_', '+', '-' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = segments.Length > 0 ? Capitalize(segments[0]) : "eShopOnWeb";
        var lastName = segments.Length > 1 ? Capitalize(string.Join(' ', segments.Skip(1))) : "Customer";

        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
