using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly BillingClaimStore _claims;
    private readonly MaxioOptions _options;
    private readonly BillingRuntimeLimits _limits;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        BillingClaimStore claims,
        IOptions<MaxioOptions> options,
        BillingRuntimeLimits limits,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _claims = claims;
        _options = options.Value;
        _limits = limits;
        _logger = logger;
    }

    public Task<SubscriptionPlanPage> ListPlansAsync(CancellationToken cancellationToken) =>
        WithBudget(ListPlansCoreAsync, cancellationToken);

    public Task<SubscribeResult> SubscribeAsync(ShopperAccount shopper, string productHandle, CancellationToken cancellationToken) =>
        WithBudget(ct => SubscribeCoreAsync(shopper, productHandle, ct), cancellationToken);

    public Task<IReadOnlyList<ShopperSubscription>> ListMineAsync(ShopperAccount shopper, CancellationToken cancellationToken) =>
        WithBudget(ct => ListMineCoreAsync(shopper, ct), cancellationToken);

    private async Task<T> WithBudget<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_limits.CallBudget);
        return await call(budget.Token);
    }

    private async Task<SubscriptionPlanPage> ListPlansCoreAsync(CancellationToken cancellationToken)
    {
        var perPage = Math.Clamp(_limits.PlanPageSize, 1, 200);
        var plans = new List<SubscriptionPlan>();
        var familyId = "handle:" + _options.ProductFamilyHandle.Trim();
        for (var page = 1; page <= _limits.MaxPlanPages; page++)
        {
            var batch = await ListProductPageAsync(familyId, page, perPage, cancellationToken);
            foreach (var item in batch)
            {
                var product = item.Product;
                if (product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                    continue;
                plans.Add(new SubscriptionPlan
                {
                    Handle = product.Handle,
                    Name = product.Name,
                    Description = product.Description,
                    PriceInCents = product.PriceInCents,
                    Interval = product.Interval,
                    IntervalUnit = product.IntervalUnit?.Value,
                    RequireCreditCard = product.RequireCreditCard
                });
            }

            if (batch.Count < perPage)
                return new SubscriptionPlanPage { Plans = plans, Truncated = false };
        }

        return new SubscriptionPlanPage { Plans = plans, Truncated = true };
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductPageAsync(string familyId, int page, int perPage, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = familyId,
                    Page = page,
                    PerPage = perPage,
                    IncludeArchived = false
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<ListProductsForProductFamilyError> ex)
        {
            LogProviderFailure("ListProductsForProductFamily", ex);
            if (ex.Error.TryGetString(out _))
                throw new SubscriptionBillingException(502, "The configured product family was not found.");
            if (ex.Error.TryGetRawError(out var raw))
                throw MapProviderStatus(raw.StatusCode);
            throw new SubscriptionBillingException(502, "Billing provider is unavailable.");
        }
        catch (ResponseDeserializationException ex)
        {
            LogProviderFailure("ListProductsForProductFamily", ex);
            throw Unreadable(ex);
        }
        catch (SdkConnectionException ex)
        {
            LogProviderFailure("ListProductsForProductFamily", ex);
            throw new SubscriptionBillingException(502, "Billing provider is unavailable.");
        }
        catch (AuthSchemeException ex)
        {
            LogProviderFailure("ListProductsForProductFamily", ex);
            throw new SubscriptionBillingException(502, "Billing provider is unavailable.");
        }
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(ShopperAccount shopper, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shopper.Email))
            throw new SubscriptionBillingException(400, "A shopper email is required to subscribe.");
        if (string.IsNullOrWhiteSpace(productHandle))
            throw new SubscriptionBillingException(400, "A plan handle is required.");

        var plans = await ListPlansCoreAsync(cancellationToken);
        var offered = plans.Plans.FirstOrDefault(plan => string.Equals(plan.Handle, productHandle, StringComparison.Ordinal));
        if (offered is null)
        {
            throw new SubscriptionBillingException(
                plans.Truncated ? 503 : 400,
                plans.Truncated
                    ? "The plan catalog is incomplete. Retry shortly."
                    : "That plan is not available.");
        }
        if (offered.RequireCreditCard == true)
            throw new SubscriptionBillingException(400, "This plan requires a payment method.");

        await SweepAsync(shopper.UserId, cancellationToken);
        var customerId = await EnsureCustomerAsync(shopper, cancellationToken);
        return await EnrollAsync(shopper, productHandle, customerId, cancellationToken);
    }

    private async Task<IReadOnlyList<ShopperSubscription>> ListMineCoreAsync(ShopperAccount shopper, CancellationToken cancellationToken)
    {
        await SweepAsync(shopper.UserId, cancellationToken);
        var customer = await LookupCustomerAsync(shopper.UserId, cancellationToken);
        if (customer.NotFound)
            return Array.Empty<ShopperSubscription>();
        if (customer.Id is not int customerId)
            throw customer.Error ?? new SubscriptionBillingException(502, "Billing provider is unavailable.");

        try
        {
            var rows = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: cancellationToken);
            return rows.Select(MapSubscription).Where(subscription => subscription is not null).Cast<ShopperSubscription>().ToList();
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            LogProviderFailure("ListCustomerSubscriptions", ex);
            return Array.Empty<ShopperSubscription>();
        }
        catch (ApiException<RawError> ex)
        {
            LogProviderFailure("ListCustomerSubscriptions", ex);
            throw MapProviderStatus(ex.StatusCode);
        }
        catch (ResponseDeserializationException ex)
        {
            LogProviderFailure("ListCustomerSubscriptions", ex);
            throw Unreadable(ex);
        }
        catch (SdkConnectionException ex)
        {
            LogProviderFailure("ListCustomerSubscriptions", ex);
            throw new SubscriptionBillingException(502, "Billing provider is unavailable.");
        }
        catch (AuthSchemeException ex)
        {
            LogProviderFailure("ListCustomerSubscriptions", ex);
            throw new SubscriptionBillingException(502, "Billing provider is unavailable.");
        }
    }

    private async Task<int> EnsureCustomerAsync(ShopperAccount shopper, CancellationToken cancellationToken)
    {
        if (!await _claims.TryInsertCustomerAsync(shopper.UserId, cancellationToken))
        {
            var existing = await _claims.GetCustomerAsync(shopper.UserId, cancellationToken);
            if (existing is null)
                throw new SubscriptionBillingException(409, "A customer enrollment is already in progress.");
            if (existing.State == BillingClaimStates.Completed && existing.MaxioCustomerId is int done)
                return done;
            if (!IsStale(existing.ClaimedAt))
                throw InFlight(existing.State);
            var settled = await LookupCustomerAsync(shopper.UserId, cancellationToken);
            if (settled.Id is int found)
            {
                await _claims.CompleteCustomerAsync(shopper.UserId, found, cancellationToken);
                return found;
            }
            if (!settled.NotFound)
                throw settled.Error ?? new SubscriptionBillingException(503, "The billing outcome is unknown. Retry shortly.");
            await _claims.DeleteCustomerAsync(shopper.UserId, cancellationToken);
            if (!await _claims.TryInsertCustomerAsync(shopper.UserId, cancellationToken))
                throw new SubscriptionBillingException(409, "A customer enrollment is already in progress.");
        }

        var prior = await LookupCustomerAsync(shopper.UserId, cancellationToken);
        if (prior.Id is int existingId)
        {
            await _claims.CompleteCustomerAsync(shopper.UserId, existingId, cancellationToken);
            return existingId;
        }
        if (!prior.NotFound)
        {
            await _claims.DeleteCustomerAsync(shopper.UserId, cancellationToken);
            throw prior.Error ?? new SubscriptionBillingException(502, "Billing provider is unavailable.");
        }

        var createdId = await CreateCustomerAsync(shopper, cancellationToken);
        await _claims.CompleteCustomerAsync(shopper.UserId, createdId, cancellationToken);
        _logger.LogInformation("Enrolled Maxio customer {MaxioCustomerId} for user {UserId}.", createdId, shopper.UserId);
        return createdId;
    }

    private async Task<int> CreateCustomerAsync(ShopperAccount shopper, CancellationToken cancellationToken)
    {
        using var write = MaxioWriteScope.Begin();
        CustomerResponse created;
        try
        {
            created = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = GivenName(shopper.Email),
                            LastName = "Shopper",
                            Email = shopper.Email,
                            Reference = shopper.UserId
                        }
                    }
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            LogProviderFailure("CreateCustomer", ex);
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var adopted = await LookupCustomerAsync(shopper.UserId, cancellationToken);
                if (adopted.Id is int id)
                    return id;
                if (adopted.NotFound)
                {
                    await _claims.DeleteCustomerAsync(shopper.UserId, cancellationToken);
                    throw MapProviderStatus(ex.StatusCode);
                }
                await _claims.MarkCustomerUnknownAsync(shopper.UserId, cancellationToken);
                throw new SubscriptionBillingException(503, "The billing outcome is unknown. Retry shortly.");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                await _claims.DeleteCustomerAsync(shopper.UserId, cancellationToken);
                throw MapProviderStatus(raw.StatusCode);
            }
            await _claims.DeleteCustomerAsync(shopper.UserId, cancellationToken);
            throw new SubscriptionBillingException(502, "Billing provider is unavailable.");
        }
        catch (ResponseDeserializationException ex)
        {
            LogProviderFailure("CreateCustomer", ex);
            if (IsSuccess(ex.StatusCode))
                return await SettleCustomerAfterUnknownWrite(shopper.UserId, cancellationToken);
            throw Unreadable(ex);
        }
        catch (SdkConnectionException ex)
        {
            LogProviderFailure("CreateCustomer", ex);
            return await SettleCustomerAfterUnknownWrite(shopper.UserId, cancellationToken);
        }
        catch (SingleSendBlockedException ex)
        {
            LogProviderFailure("CreateCustomer", ex);
            return await SettleCustomerAfterUnknownWrite(shopper.UserId, cancellationToken);
        }
        catch (AuthSchemeException ex)
        {
            LogProviderFailure("CreateCustomer", ex);
            await _claims.DeleteCustomerAsync(shopper.UserId, cancellationToken);
            throw new SubscriptionBillingException(502, "Billing provider is unavailable.");
        }

        if (created.Customer?.Id is not int newId)
            return await SettleCustomerAfterUnknownWrite(shopper.UserId, cancellationToken);
        return newId;
    }

    private async Task<int> SettleCustomerAfterUnknownWrite(string userId, CancellationToken cancellationToken)
    {
        var lookup = await LookupCustomerAsync(userId, cancellationToken);
        if (lookup.Id is int id)
            return id;
        await _claims.MarkCustomerUnknownAsync(userId, cancellationToken);
        throw new SubscriptionBillingException(503, "The billing outcome is unknown. Retry shortly.");
    }

    private async Task<SubscribeResult> EnrollAsync(ShopperAccount shopper, string productHandle, int customerId, CancellationToken cancellationToken)
    {
        var reference = SubscriptionReference(shopper.UserId, productHandle);
        if (!await _claims.TryInsertSubscriptionAsync(reference, shopper.UserId, productHandle, cancellationToken))
        {
            var existing = await _claims.GetSubscriptionAsync(reference, cancellationToken);
            if (existing is null)
                throw new SubscriptionBillingException(409, "A subscription enrollment is already in progress.");
            if (existing.State == BillingClaimStates.Completed)
            {
                var current = await RequireSubscriptionAsync(reference, cancellationToken);
                return new SubscribeResult { Subscription = current, Created = false };
            }
            if (!IsStale(existing.ClaimedAt))
                throw InFlight(existing.State);
            var settled = await LookupSubscriptionAsync(reference, cancellationToken);
            if (settled.Subscription?.Id is int settledId)
            {
                await _claims.CompleteSubscriptionAsync(reference, settledId, cancellationToken);
                return new SubscribeResult { Subscription = settled.Subscription, Created = false };
            }
            if (!settled.NotFound)
                throw settled.Error ?? new SubscriptionBillingException(503, "The billing outcome is unknown. Retry shortly.");
            await _claims.DeleteSubscriptionAsync(reference, cancellationToken);
            if (!await _claims.TryInsertSubscriptionAsync(reference, shopper.UserId, productHandle, cancellationToken))
                throw new SubscriptionBillingException(409, "A subscription enrollment is already in progress.");
        }

        var prior = await LookupSubscriptionAsync(reference, cancellationToken);
        if (prior.Subscription?.Id is int priorId)
        {
            await _claims.CompleteSubscriptionAsync(reference, priorId, cancellationToken);
            return new SubscribeResult { Subscription = prior.Subscription, Created = false };
        }
        if (!prior.NotFound)
        {
            await _claims.DeleteSubscriptionAsync(reference, cancellationToken);
            throw prior.Error ?? new SubscriptionBillingException(502, "Billing provider is unavailable.");
        }

        var created = await CreateSubscriptionAsync(customerId, productHandle, reference, cancellationToken);
        if (created.Id is not int createdId)
            throw new SubscriptionBillingException(503, "The billing outcome is unknown. Retry shortly.");
        await _claims.CompleteSubscriptionAsync(reference, createdId, cancellationToken);
        _logger.LogInformation(
            "Enrolled subscription {MaxioSubscriptionId} on {ProductHandle} for user {UserId} in state {State}.",
            createdId,
            productHandle,
            shopper.UserId,
            created.State);
        return new SubscribeResult { Subscription = created, Created = true };
    }

    private async Task<ShopperSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        using var write = MaxioWriteScope.Begin();
        SubscriptionResponse created;
        try
        {
            created = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = productHandle,
                            CustomerId = customerId,
                            Reference = reference,
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            LogProviderFailure("CreateSubscription", ex);
            if (ex.Error.TryGetErrorListResponse1(out var rejected))
            {
                _logger.LogWarning(
                    "Maxio CreateSubscription rejected the enrollment: {ProviderErrors}",
                    string.Join(" | ", rejected.Errors));
                var adopted = await LookupSubscriptionAsync(reference, cancellationToken);
                if (adopted.Subscription is not null)
                    return adopted.Subscription;
                if (adopted.NotFound)
                {
                    await _claims.DeleteSubscriptionAsync(reference, cancellationToken);
                    throw MapProviderStatus(ex.StatusCode);
                }
                await _claims.MarkSubscriptionUnknownAsync(reference, cancellationToken);
                throw new SubscriptionBillingException(503, "The billing outcome is unknown. Retry shortly.");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                await _claims.DeleteSubscriptionAsync(reference, cancellationToken);
                throw MapProviderStatus(raw.StatusCode);
            }
            await _claims.DeleteSubscriptionAsync(reference, cancellationToken);
            throw new SubscriptionBillingException(502, "Billing provider is unavailable.");
        }
        catch (ResponseDeserializationException ex)
        {
            LogProviderFailure("CreateSubscription", ex);
            if (IsSuccess(ex.StatusCode))
                return await SettleSubscriptionAfterUnknownWrite(reference, cancellationToken);
            throw Unreadable(ex);
        }
        catch (SdkConnectionException ex)
        {
            LogProviderFailure("CreateSubscription", ex);
            return await SettleSubscriptionAfterUnknownWrite(reference, cancellationToken);
        }
        catch (SingleSendBlockedException ex)
        {
            LogProviderFailure("CreateSubscription", ex);
            return await SettleSubscriptionAfterUnknownWrite(reference, cancellationToken);
        }
        catch (AuthSchemeException ex)
        {
            LogProviderFailure("CreateSubscription", ex);
            await _claims.DeleteSubscriptionAsync(reference, cancellationToken);
            throw new SubscriptionBillingException(502, "Billing provider is unavailable.");
        }

        var mapped = MapSubscription(created);
        if (mapped?.Id is null)
            return await SettleSubscriptionAfterUnknownWrite(reference, cancellationToken);
        return mapped;
    }

    private async Task<ShopperSubscription> SettleSubscriptionAfterUnknownWrite(string reference, CancellationToken cancellationToken)
    {
        var lookup = await LookupSubscriptionAsync(reference, cancellationToken);
        if (lookup.Subscription is not null)
            return lookup.Subscription;
        await _claims.MarkSubscriptionUnknownAsync(reference, cancellationToken);
        throw new SubscriptionBillingException(503, "The billing outcome is unknown. Retry shortly.");
    }

    private async Task<ShopperSubscription> RequireSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var lookup = await LookupSubscriptionAsync(reference, cancellationToken);
        if (lookup.Subscription is not null)
            return lookup.Subscription;
        if (lookup.NotFound)
            throw new SubscriptionBillingException(404, "The subscription is no longer available.");
        throw lookup.Error ?? new SubscriptionBillingException(502, "Billing provider is unavailable.");
    }

    private async Task SweepAsync(string userId, CancellationToken cancellationToken)
    {
        var customer = await _claims.GetCustomerAsync(userId, cancellationToken);
        if (customer is not null && customer.State != BillingClaimStates.Completed)
        {
            var lookup = await LookupCustomerAsync(userId, cancellationToken);
            if (lookup.Id is int id)
                await _claims.CompleteCustomerAsync(userId, id, cancellationToken);
            else if (lookup.NotFound && IsStale(customer.ClaimedAt))
                await _claims.DeleteCustomerAsync(userId, cancellationToken);
        }

        foreach (var claim in await _claims.ListSubscriptionsAsync(userId, cancellationToken))
        {
            if (claim.State == BillingClaimStates.Completed)
                continue;
            var lookup = await LookupSubscriptionAsync(claim.Reference, cancellationToken);
            if (lookup.Subscription?.Id is int id)
                await _claims.CompleteSubscriptionAsync(claim.Reference, id, cancellationToken);
            else if (lookup.NotFound && IsStale(claim.ClaimedAt))
                await _claims.DeleteSubscriptionAsync(claim.Reference, cancellationToken);
        }
    }

    private async Task<ProviderIdLookup> LookupCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: cancellationToken);
            if (response.Customer?.Id is int id)
                return ProviderIdLookup.Found(id);
            return ProviderIdLookup.Failed(new SubscriptionBillingException(502, "The billing provider returned a response that could not be processed."));
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return ProviderIdLookup.Missing();
        }
        catch (ApiException<RawError> ex)
        {
            LogProviderFailure("ReadCustomerByReference", ex);
            return ProviderIdLookup.Failed(MapProviderStatus(ex.StatusCode));
        }
        catch (ResponseDeserializationException ex)
        {
            LogProviderFailure("ReadCustomerByReference", ex);
            return ProviderIdLookup.Failed(Unreadable(ex));
        }
        catch (SdkConnectionException ex)
        {
            LogProviderFailure("ReadCustomerByReference", ex);
            return ProviderIdLookup.Failed(new SubscriptionBillingException(502, "Billing provider is unavailable."));
        }
        catch (AuthSchemeException ex)
        {
            LogProviderFailure("ReadCustomerByReference", ex);
            return ProviderIdLookup.Failed(new SubscriptionBillingException(502, "Billing provider is unavailable."));
        }
    }

    private async Task<ProviderSubscriptionLookup> LookupSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: cancellationToken);
            var mapped = MapSubscription(response);
            if (mapped is null)
                return ProviderSubscriptionLookup.Failed(new SubscriptionBillingException(502, "The billing provider returned a response that could not be processed."));
            return ProviderSubscriptionLookup.Found(mapped);
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            LogProviderFailure("FindSubscription", ex);
            if (ex.Error.TryGetNoContent(out _))
                return ProviderSubscriptionLookup.Missing();
            if (ex.Error.TryGetRawError(out var raw))
                return ProviderSubscriptionLookup.Failed(MapProviderStatus(raw.StatusCode));
            return ProviderSubscriptionLookup.Failed(new SubscriptionBillingException(502, "Billing provider is unavailable."));
        }
        catch (ResponseDeserializationException ex)
        {
            LogProviderFailure("FindSubscription", ex);
            return ProviderSubscriptionLookup.Failed(Unreadable(ex));
        }
        catch (SdkConnectionException ex)
        {
            LogProviderFailure("FindSubscription", ex);
            return ProviderSubscriptionLookup.Failed(new SubscriptionBillingException(502, "Billing provider is unavailable."));
        }
        catch (AuthSchemeException ex)
        {
            LogProviderFailure("FindSubscription", ex);
            return ProviderSubscriptionLookup.Failed(new SubscriptionBillingException(502, "Billing provider is unavailable."));
        }
    }

    private static ShopperSubscription? MapSubscription(SubscriptionResponse response)
    {
        var subscription = response.Subscription;
        if (subscription is null)
            return null;
        return new ShopperSubscription
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents,
            Currency = subscription.Currency,
            State = subscription.State?.Value,
            NextBillingAt = subscription.CurrentPeriodEndsAt
        };
    }

    private bool IsStale(DateTimeOffset claimedAt) => claimedAt <= DateTimeOffset.UtcNow - _limits.StaleClaimWindow;

    private static string SubscriptionReference(string userId, string productHandle) => $"{userId}:{productHandle}";

    private static string GivenName(string email)
    {
        var at = email.IndexOf('@');
        var local = (at > 0 ? email[..at] : email).Trim();
        if (local.Length == 0)
            return "Shopper";
        return local.Length <= 40 ? local : local[..40];
    }

    private static SubscriptionBillingException InFlight(string state) =>
        state == BillingClaimStates.Unknown
            ? new SubscriptionBillingException(503, "The billing outcome is unknown. Retry shortly.")
            : new SubscriptionBillingException(409, "A billing enrollment is already in progress.");

    private static bool IsSuccess(HttpStatusCode status)
    {
        var code = (int)status;
        return code >= 200 && code < 300;
    }

    private static SubscriptionBillingException Unreadable(ResponseDeserializationException exception)
    {
        if (IsSuccess(exception.StatusCode))
            return new SubscriptionBillingException(502, "The billing provider returned a response that could not be processed.");
        var code = (int)exception.StatusCode;
        if (code is 401 or 403 or >= 500)
            return new SubscriptionBillingException(502, "The billing provider returned a response that could not be processed.");
        if (code == 429)
            return new SubscriptionBillingException(503, "Billing provider is temporarily unavailable.");
        return new SubscriptionBillingException(code, "The billing provider rejected the request.");
    }

    private static SubscriptionBillingException MapProviderStatus(HttpStatusCode status)
    {
        var code = (int)status;
        if (code is 401 or 403)
            return new SubscriptionBillingException(502, "Billing provider is unavailable.");
        if (code == 429)
            return new SubscriptionBillingException(503, "Billing provider is temporarily unavailable.");
        if (code == 404)
            return new SubscriptionBillingException(404, "The billing record was not found.");
        if (code >= 400 && code < 500)
            return new SubscriptionBillingException(code == 422 ? 400 : code, "The billing provider rejected the request.");
        return new SubscriptionBillingException(502, "Billing provider is unavailable.");
    }

    private void LogProviderFailure(string operation, Exception exception)
    {
        if (exception is ApiException api)
            _logger.LogWarning(api, "Maxio {Operation} failed with status {StatusCode}.", operation, (int)api.StatusCode);
        else
            _logger.LogWarning(exception, "Maxio {Operation} failed.", operation);
    }

    private readonly record struct ProviderIdLookup(int? Id, bool NotFound, SubscriptionBillingException? Error)
    {
        public static ProviderIdLookup Found(int id) => new(id, false, null);
        public static ProviderIdLookup Missing() => new(null, true, null);
        public static ProviderIdLookup Failed(SubscriptionBillingException error) => new(null, false, error);
    }

    private readonly record struct ProviderSubscriptionLookup(ShopperSubscription? Subscription, bool NotFound, SubscriptionBillingException? Error)
    {
        public static ProviderSubscriptionLookup Found(ShopperSubscription subscription) => new(subscription, false, null);
        public static ProviderSubscriptionLookup Missing() => new(null, true, null);
        public static ProviderSubscriptionLookup Failed(SubscriptionBillingException error) => new(null, false, error);
    }
}
