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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string DefaultProductHandle = "eshop-pro";
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollGates = new();

    private static readonly HashSet<SubscriptionState> TerminalStates =
    [
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.TrialEnded,
        SubscriptionState.FailedToCreate,
    ];

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var familyId = "handle:" + _options.ProductFamilyHandle;
        var plans = new List<SubscriptionPlan>();
        var page = 1;
        const int perPage = 200;

        while (true)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await Bounded(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyId,
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
                        ct: ct),
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw MapListProductsError(ex);
            }
            catch (Exception ex) when (IsBoundaryException(ex, cancellationToken))
            {
                throw MapBoundaryException(ex);
            }

            foreach (var envelope in pageItems)
            {
                var product = envelope.Product;
                if (product.ArchivedAt is not null)
                {
                    continue;
                }

                if (product.ProductFamily?.Handle is { } familyHandle
                    && !string.Equals(familyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(MapPlan(product));
            }

            if (pageItems.Count < perPage)
            {
                break;
            }

            page++;
        }

        return plans;
    }

    public async Task<ShopperSubscription> SubscribeAsync(
        ShopperIdentity shopper,
        string? productHandle,
        CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var resolvedHandle = ResolveProductHandle(productHandle, plans);
        var gate = EnrollGates.GetOrAdd($"{shopper.UserId}:{resolvedHandle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await EnrollAsync(shopper, resolvedHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var customer = await TryReadCustomerByReferenceAsync(userId, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<ShopperSubscription>();
        }

        var envelopes = await ListCustomerSubscriptionsAsync(customer.Id.Value, cancellationToken);
        var result = new List<ShopperSubscription>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is { } subscription)
            {
                result.Add(MapSubscription(subscription));
            }
        }

        return result;
    }

    private async Task<ShopperSubscription> EnrollAsync(
        ShopperIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var customer = await FindOrCreateCustomerAsync(shopper, cancellationToken);
        var existing = await FindLiveEnrollmentAsync(shopper.UserId, productHandle, customer.Id, cancellationToken);
        if (existing is not null)
        {
            return MapSubscription(existing);
        }

        try
        {
            using var writeScope = OnceWriteScope.Begin();
            var created = await Bounded(
                ct => _client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customer.Id,
                            CustomerReference = shopper.UserId,
                            Reference = SubscriptionReference(shopper.UserId, productHandle),
                            PaymentCollectionMethod = CollectionMethod.Remittance,
                        }
                    },
                    ct: ct),
                cancellationToken);

            if (created.Subscription is null)
            {
                throw new SubscriptionBillingException(
                    502,
                    "The billing provider returned a response that could not be processed.");
            }

            return MapSubscription(created.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var recovered = await RecoverEnrollmentAsync(shopper.UserId, productHandle, customer.Id, cancellationToken);
            if (recovered is not null)
            {
                return MapSubscription(recovered);
            }

            throw MapCreateSubscriptionError(ex);
        }
        catch (DuplicateWriteRefusedException)
        {
            var recovered = await RecoverEnrollmentAsync(shopper.UserId, productHandle, customer.Id, cancellationToken);
            if (recovered is not null)
            {
                return MapSubscription(recovered);
            }

            throw new SubscriptionBillingException(
                502,
                "The billing provider did not confirm the subscription. Please retry.");
        }
        catch (JsonException ex)
        {
            var recovered = await RecoverEnrollmentAsync(shopper.UserId, productHandle, customer.Id, cancellationToken);
            if (recovered is not null)
            {
                return MapSubscription(recovered);
            }

            throw MapJsonException(ex, "The billing request was rejected.");
        }
        catch (Exception ex) when (IsBoundaryException(ex, cancellationToken))
        {
            var recovered = await RecoverEnrollmentAsync(shopper.UserId, productHandle, customer.Id, cancellationToken);
            if (recovered is not null)
            {
                return MapSubscription(recovered);
            }

            throw MapBoundaryException(ex);
        }
    }

    private async Task<Customer> FindOrCreateCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            using var writeScope = OnceWriteScope.Begin();
            var created = await Bounded(
                ct => _client.Customers.CreateCustomer(
                    body: new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = shopper.FirstName,
                            LastName = shopper.LastName,
                            Email = shopper.Email,
                            Reference = shopper.UserId,
                        }
                    },
                    ct: ct),
                cancellationToken);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _) || IsUnprocessable(ex))
            {
                return await ReadCustomerAfterCreateConflictAsync(shopper.UserId, cancellationToken, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRaw(raw, "The billing provider could not create the customer.");
            }

            throw new SubscriptionBillingException(502, "The billing provider could not create the customer.", ex);
        }
        catch (DuplicateWriteRefusedException)
        {
            return await ReadCustomerAfterCreateConflictAsync(shopper.UserId, cancellationToken, inner: null);
        }
        catch (JsonException ex)
        {
            if (LastHttpStatus.Current == HttpStatusCode.UnprocessableEntity)
            {
                return await ReadCustomerAfterCreateConflictAsync(shopper.UserId, cancellationToken, ex);
            }

            throw MapJsonException(ex, "The billing request was rejected.");
        }
        catch (Exception ex) when (IsBoundaryException(ex, cancellationToken))
        {
            var recovered = await TryReadCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw MapBoundaryException(ex);
        }
    }

    private async Task<Customer> ReadCustomerAfterCreateConflictAsync(
        string userId,
        CancellationToken cancellationToken,
        Exception? inner)
    {
        var recovered = await TryReadCustomerByReferenceAsync(userId, cancellationToken);
        if (recovered is not null)
        {
            return recovered;
        }

        throw new SubscriptionBillingException(
            422,
            "The billing request was rejected.",
            inner);
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Customers.ReadCustomerByReference(reference: userId, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "The billing provider could not look up the customer.");
        }
        catch (JsonException ex)
        {
            throw MapJsonException(ex, "The billing provider could not look up the customer.");
        }
        catch (Exception ex) when (IsBoundaryException(ex, cancellationToken))
        {
            throw MapBoundaryException(ex);
        }
    }

    private async Task<Subscription?> FindLiveEnrollmentAsync(
        string userId,
        string productHandle,
        int? customerId,
        CancellationToken cancellationToken)
    {
        var byReference = await TryFindSubscriptionByReferenceAsync(
            SubscriptionReference(userId, productHandle),
            cancellationToken);
        if (byReference is not null && IsLive(byReference.State) && MatchesProduct(byReference, productHandle))
        {
            return byReference;
        }

        if (customerId is null)
        {
            return byReference is not null && IsLive(byReference.State) ? byReference : null;
        }

        var listed = await ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);
        foreach (var envelope in listed)
        {
            var subscription = envelope.Subscription;
            if (subscription is not null
                && MatchesProduct(subscription, productHandle)
                && IsLive(subscription.State))
            {
                return subscription;
            }
        }

        return null;
    }

    private Task<Subscription?> RecoverEnrollmentAsync(
        string userId,
        string productHandle,
        int? customerId,
        CancellationToken cancellationToken)
        => FindLiveEnrollmentAsync(userId, productHandle, customerId, cancellationToken);

    private async Task<Subscription?> TryFindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct),
                cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw MapRaw(raw, "The billing provider could not look up the subscription.");
            }

            throw new SubscriptionBillingException(502, "The billing provider could not look up the subscription.", ex);
        }
        catch (JsonException ex)
        {
            if (LastHttpStatus.Current == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw MapJsonException(ex, "The billing provider could not look up the subscription.");
        }
        catch (Exception ex) when (IsBoundaryException(ex, cancellationToken))
        {
            throw MapBoundaryException(ex);
        }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(
                ct => _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<SubscriptionResponse>();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRaw(ex.Error, "The billing provider could not list subscriptions.");
        }
        catch (JsonException ex)
        {
            throw MapJsonException(ex, "The billing provider could not list subscriptions.");
        }
        catch (Exception ex) when (IsBoundaryException(ex, cancellationToken))
        {
            throw MapBoundaryException(ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        LastHttpStatus.Current = null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SubscriptionBillingException(504, "The billing provider timed out.");
        }
    }

    private string ResolveProductHandle(string? requested, IReadOnlyList<SubscriptionPlan> plans)
    {
        if (plans.Count == 0)
        {
            throw new SubscriptionBillingException(502, "No subscription plans are available.");
        }

        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = plans.FirstOrDefault(p =>
                string.Equals(p.Handle, requested, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new SubscriptionBillingException(400, $"Unknown subscription plan '{requested}'.");
            }

            return match.Handle;
        }

        var fallback = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, DefaultProductHandle, StringComparison.OrdinalIgnoreCase));
        return fallback?.Handle ?? plans[0].Handle;
    }

    private static string SubscriptionReference(string userId, string productHandle)
        => $"{userId}:{productHandle}";

    private static bool MatchesProduct(Subscription subscription, string productHandle)
        => string.Equals(subscription.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase);

    private static bool IsLive(SubscriptionState? state)
        => state is null || !TerminalStates.Contains(state);

    private static SubscriptionPlan MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        Price = CentsToAmount(product.PriceInCents),
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value,
        RequireCreditCard = product.RequireCreditCard ?? false,
    };

    private static ShopperSubscription MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        State = subscription.State?.Value ?? "unknown",
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
        Price = CentsToAmount(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit?.Value,
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
    };

    private static decimal CentsToAmount(long? cents)
        => (cents ?? 0) / 100m;

    private SubscriptionBillingException MapListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            _logger.LogWarning("Maxio product family {Family} was not found.", _options.ProductFamilyHandle);
            return new SubscriptionBillingException(502, "The configured product family was not found.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "The billing provider could not list plans.");
        }

        return new SubscriptionBillingException(502, "The billing provider could not list plans.", ex);
    }

    private static SubscriptionBillingException MapCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errors) && errors.Errors.Count > 0)
        {
            return new SubscriptionBillingException(422, string.Join(" ", errors.Errors), ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "The billing request was rejected.");
        }

        return new SubscriptionBillingException(422, "The billing request was rejected.", ex);
    }

    private static bool IsUnprocessable(SdkException<CreateCustomerError> ex)
        => ex.Error.TryGetRawError(out var raw) && raw.StatusCode == HttpStatusCode.UnprocessableEntity;

    private static SubscriptionBillingException MapRaw(RawError raw, string fallback)
    {
        var code = (int)raw.StatusCode;
        if (code is 401 or 403)
        {
            return new SubscriptionBillingException(502, "The billing provider rejected the server credentials.");
        }

        if (code >= 500)
        {
            return new SubscriptionBillingException(502, "The billing provider is unavailable.");
        }

        if (code == 404)
        {
            return new SubscriptionBillingException(404, fallback);
        }

        if (code == 422)
        {
            return new SubscriptionBillingException(422, "The billing request was rejected.");
        }

        return new SubscriptionBillingException(502, fallback);
    }

    private static SubscriptionBillingException MapJsonException(JsonException ex, string rejectionMessage)
    {
        var status = LastHttpStatus.Current;
        if (status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
        {
            var code = status == HttpStatusCode.UnprocessableEntity ? 422 : (int)status.Value;
            return new SubscriptionBillingException(code, rejectionMessage, ex);
        }

        return new SubscriptionBillingException(
            502,
            "The billing provider returned a response that could not be processed.",
            ex);
    }

    private static bool IsBoundaryException(Exception ex, CancellationToken cancellationToken)
        => ex is DuplicateWriteRefusedException
            || ex is HttpRequestException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            || ex is SubscriptionBillingException;

    private static SubscriptionBillingException MapBoundaryException(Exception ex)
    {
        if (ex is SubscriptionBillingException billing)
        {
            return billing;
        }

        if (ex is DuplicateWriteRefusedException)
        {
            return new SubscriptionBillingException(
                502,
                "The billing provider did not confirm the request. Please retry.",
                ex);
        }

        return new SubscriptionBillingException(502, "The billing provider is unavailable.", ex);
    }
}
