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
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    public const int PageSize = 200;
    public const int PageCap = 5;
    public static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        CatalogContext db,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PlanCatalog> ListPlansAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        using var cts = Begin(cancellationToken);
        try
        {
            var listed = await LoadOfferedPlansAsync(correlationId, cts.Token);
            return new PlanCatalog
            {
                Plans = listed.Plans.Select(ToPlan).ToList(),
                Truncated = listed.Truncated
            };
        }
        catch (Exception ex) when (ex is not SubscriptionBillingException && ex is not OperationCanceledException)
        {
            throw MapUnhandled(ex, "ListProductsForProductFamily", correlationId);
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(ShopperProfile shopper, string? planHandle, Guid correlationId, CancellationToken cancellationToken)
    {
        if (shopper == null) throw new ArgumentNullException(nameof(shopper));
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionBillingException("A plan handle is required.", 400);

        using var cts = Begin(cancellationToken);
        var ct = cts.Token;
        try
        {
            var catalog = await LoadOfferedPlansAsync(correlationId, ct);
            if (!catalog.ByHandle.TryGetValue(planHandle, out var offered))
                throw new SubscriptionBillingException("That plan is not available.", 400);
            if (offered.RequireCreditCard == true)
                throw new SubscriptionBillingException("This plan requires a payment method.", 400);

            _logger.LogInformation(
                "Billing subscribe correlation {CorrelationId} customer {CustomerReference} plan {PlanHandle}",
                correlationId, shopper.UserId, planHandle);

            await EnsureCustomerAsync(shopper, correlationId, ct);
            return await EnrollAsync(shopper.UserId, planHandle, correlationId, ct);
        }
        catch (Exception ex) when (ex is not SubscriptionBillingException && ex is not DuplicateException && ex is not OperationCanceledException)
        {
            throw MapUnhandled(ex, "CreateSubscription", correlationId);
        }
    }

    public async Task<IReadOnlyList<ShopperSubscription>> ListMineAsync(string userId, Guid correlationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new SubscriptionBillingException("The caller is not signed in.", 401);

        using var cts = Begin(cancellationToken);
        var ct = cts.Token;
        try
        {
            Customer? customer;
            try
            {
                customer = await ReadCustomerAsync(userId, ct);
            }
            catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return Array.Empty<ShopperSubscription>();
            }

            if (customer?.Id == null)
                throw new SubscriptionBillingException("The billing provider returned a customer that could not be read.", 502);

            var listed = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customer.Id.Value },
                cancellationToken: ct);

            _logger.LogInformation(
                "Billing ListCustomerSubscriptions correlation {CorrelationId} customer {CustomerReference}",
                correlationId, userId);

            return listed.Select(item => MapSubscription(item.Subscription)).ToList();
        }
        catch (Exception ex) when (ex is not SubscriptionBillingException && ex is not OperationCanceledException)
        {
            throw MapUnhandled(ex, "ListCustomerSubscriptions", correlationId);
        }
    }

    private async Task EnsureCustomerAsync(ShopperProfile shopper, Guid correlationId, CancellationToken cancellationToken)
    {
        var acquisition = await TryClaimAsync(shopper.UserId, cancellationToken);
        if (!acquisition.Inserted)
        {
            await ResumeCustomerClaimAsync(acquisition.Claim, shopper.UserId, correlationId, cancellationToken);
            return;
        }

        var claim = acquisition.Claim;
        try
        {
            var existing = await ReadCustomerAsync(shopper.UserId, cancellationToken);
            await CompleteAsync(claim, cancellationToken);
            if (existing.Id == null)
                _logger.LogWarning(
                    "Billing ReadCustomerByReference returned no id correlation {CorrelationId} customer {CustomerReference}",
                    correlationId, shopper.UserId);
            return;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Absent. Fall through to create.
        }
        catch (SdkConnectionException)
        {
            await MarkUnknownAsync(claim, cancellationToken);
            throw new BillingOutcomeUnknownException();
        }
        catch (ResponseDeserializationException)
        {
            await MarkUnknownAsync(claim, cancellationToken);
            throw new BillingOutcomeUnknownException();
        }
        catch (OperationCanceledException)
        {
            await ReleaseAsync(claim, CancellationToken.None);
            throw;
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = shopper.FirstName,
                            LastName = shopper.LastName,
                            Email = shopper.Email,
                            Reference = shopper.UserId
                        }
                    }
                },
                cancellationToken: cancellationToken);

            await CompleteAsync(claim, cancellationToken);
            _logger.LogInformation(
                "Billing CreateCustomer correlation {CorrelationId} customer {CustomerReference}",
                correlationId, shopper.UserId);
            _ = created.Customer;
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            await ReconcileRejectedCustomerAsync(claim, shopper.UserId, correlationId, ex, cancellationToken);
        }
        catch (SdkConnectionException)
        {
            await SettleCustomerAfterDisconnectAsync(claim, shopper.UserId, correlationId, cancellationToken);
        }
        catch (ResponseDeserializationException ex) when (IsSuccess(ex.StatusCode))
        {
            await SettleCustomerAfterDisconnectAsync(claim, shopper.UserId, correlationId, cancellationToken);
        }
        catch (ResponseDeserializationException ex)
        {
            await ReleaseAsync(claim, cancellationToken);
            throw Unreadable(ex, "CreateCustomer", correlationId);
        }
        catch (OperationCanceledException)
        {
            await SettleCustomerAfterDisconnectAsync(claim, shopper.UserId, correlationId, CancellationToken.None);
        }
        catch (AuthSchemeException ex)
        {
            await ReleaseAsync(claim, cancellationToken);
            throw Unavailable("CreateCustomer", correlationId, ex);
        }
    }

    private async Task ResumeCustomerClaimAsync(BillingClaim claim, string userId, Guid correlationId, CancellationToken cancellationToken)
    {
        if (claim.Status == BillingClaimStatus.InProgress)
            throw new DuplicateException("A billing request for this account is already in progress.");

        if (claim.Status == BillingClaimStatus.Completed)
        {
            try
            {
                await ReadCustomerAsync(userId, cancellationToken);
                return;
            }
            catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new SubscriptionBillingException("The billing customer for this account could not be read.", 502);
            }
        }

        await SettleCustomerAfterDisconnectAsync(claim, userId, correlationId, cancellationToken);
    }

    private async Task ReconcileRejectedCustomerAsync(
        BillingClaim claim,
        string userId,
        Guid correlationId,
        ApiException<CreateCustomerError> exception,
        CancellationToken cancellationToken)
    {
        var message = "The billing provider rejected the customer.";
        if (exception.Error.TryGetCustomerErrorResponse1(out var body))
            message = Describe(body.Errors);
        else if (exception.Error.TryGetRawError(out _))
            message = "The billing provider rejected the customer.";

        _logger.LogWarning(
            "Billing CreateCustomer returned {StatusCode} correlation {CorrelationId} customer {CustomerReference}: {Error}",
            (int)exception.StatusCode, correlationId, userId, message);

        if (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            try
            {
                var found = await ReadCustomerAsync(userId, cancellationToken);
                await CompleteAsync(claim, cancellationToken);
                _ = found;
                return;
            }
            catch (ApiException<RawError> read) when (read.StatusCode == HttpStatusCode.NotFound)
            {
                await ReleaseAsync(claim, cancellationToken);
                throw new SubscriptionBillingException(message, 422, exception);
            }
            catch (Exception)
            {
                await MarkUnknownAsync(claim, cancellationToken);
                throw new BillingOutcomeUnknownException();
            }
        }

        await ReleaseAsync(claim, cancellationToken);
        throw new SubscriptionBillingException(message, MapStatus((int)exception.StatusCode), exception);
    }

    private async Task SettleCustomerAfterDisconnectAsync(BillingClaim claim, string userId, Guid correlationId, CancellationToken cancellationToken)
    {
        try
        {
            var found = await ReadCustomerAsync(userId, FreshSettleToken(cancellationToken));
            await CompleteAsync(claim, cancellationToken);
            _logger.LogInformation(
                "Billing CreateCustomer settled by ReadCustomerByReference correlation {CorrelationId} customer {CustomerReference}",
                correlationId, userId);
            _ = found;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            await MarkUnknownAsync(claim, cancellationToken);
            throw new BillingOutcomeUnknownException();
        }
        catch (Exception)
        {
            await MarkUnknownAsync(claim, CancellationToken.None);
            throw new BillingOutcomeUnknownException();
        }
    }

    private async Task<SubscribeResult> EnrollAsync(string userId, string planHandle, Guid correlationId, CancellationToken cancellationToken)
    {
        var reference = SubscriptionReference.For(userId, planHandle);
        var acquisition = await TryClaimAsync(reference, cancellationToken);
        if (!acquisition.Inserted)
            return await ResumeSubscriptionClaimAsync(acquisition.Claim, reference, planHandle, correlationId, cancellationToken);

        var claim = acquisition.Claim;
        try
        {
            var existing = await FindSubscriptionAsync(reference, cancellationToken);
            if (existing != null)
            {
                if (!HandleMatches(existing, planHandle))
                {
                    await MarkUnknownAsync(claim, cancellationToken);
                    throw new BillingOutcomeUnknownException();
                }

                await CompleteAsync(claim, cancellationToken);
                return new SubscribeResult { Subscription = MapSubscription(existing), Created = false };
            }
        }
        catch (BillingOutcomeUnknownException)
        {
            throw;
        }
        catch (SubscriptionBillingException)
        {
            await ReleaseAsync(claim, cancellationToken);
            throw;
        }
        catch (SdkConnectionException)
        {
            await MarkUnknownAsync(claim, cancellationToken);
            throw new BillingOutcomeUnknownException();
        }
        catch (ResponseDeserializationException)
        {
            await MarkUnknownAsync(claim, cancellationToken);
            throw new BillingOutcomeUnknownException();
        }
        catch (OperationCanceledException)
        {
            await ReleaseAsync(claim, CancellationToken.None);
            throw;
        }

        try
        {
            var created = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerReference = userId,
                            Reference = reference,
                            // Automatic collection charges the opening balance immediately and
                            // rejects the signup when the product does not require a card but
                            // none is on file. Remittance invoices that balance instead.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                },
                cancellationToken: cancellationToken);

            await CompleteAsync(claim, cancellationToken);
            _logger.LogInformation(
                "Billing CreateSubscription correlation {CorrelationId} customer {CustomerReference} plan {PlanHandle} subscription {SubscriptionReference}",
                correlationId, userId, planHandle, reference);
            return new SubscribeResult
            {
                Subscription = MapSubscription(created.Subscription),
                Created = true
            };
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            return await ReconcileRejectedSubscriptionAsync(claim, reference, planHandle, correlationId, ex, cancellationToken);
        }
        catch (SdkConnectionException)
        {
            return await SettleSubscriptionAfterDisconnectAsync(claim, reference, planHandle, correlationId, cancellationToken);
        }
        catch (ResponseDeserializationException ex) when (IsSuccess(ex.StatusCode))
        {
            return await SettleSubscriptionAfterDisconnectAsync(claim, reference, planHandle, correlationId, cancellationToken);
        }
        catch (ResponseDeserializationException ex)
        {
            await ReleaseAsync(claim, cancellationToken);
            throw Unreadable(ex, "CreateSubscription", correlationId);
        }
        catch (OperationCanceledException)
        {
            return await SettleSubscriptionAfterDisconnectAsync(claim, reference, planHandle, correlationId, CancellationToken.None);
        }
        catch (AuthSchemeException ex)
        {
            await ReleaseAsync(claim, cancellationToken);
            throw Unavailable("CreateSubscription", correlationId, ex);
        }
    }

    private async Task<SubscribeResult> ResumeSubscriptionClaimAsync(
        BillingClaim claim,
        string reference,
        string planHandle,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (claim.Status == BillingClaimStatus.InProgress)
            throw new DuplicateException("A subscription request for this plan is already in progress.");

        if (claim.Status == BillingClaimStatus.Completed)
        {
            var found = await FindSubscriptionAsync(reference, cancellationToken);
            if (found == null || !HandleMatches(found, planHandle))
                throw new SubscriptionBillingException("The subscription for this plan could not be read.", 502);
            return new SubscribeResult { Subscription = MapSubscription(found), Created = false };
        }

        return await SettleSubscriptionAfterDisconnectAsync(claim, reference, planHandle, correlationId, cancellationToken);
    }

    private async Task<SubscribeResult> ReconcileRejectedSubscriptionAsync(
        BillingClaim claim,
        string reference,
        string planHandle,
        Guid correlationId,
        ApiException<CreateSubscriptionError> exception,
        CancellationToken cancellationToken)
    {
        var message = "The billing provider rejected the subscription.";
        if (exception.Error.TryGetErrorListResponse1(out var body))
            message = string.Join(" ", body.Errors);
        else if (!exception.Error.TryGetRawError(out _))
            message = "The billing provider rejected the subscription.";

        _logger.LogWarning(
            "Billing CreateSubscription returned {StatusCode} correlation {CorrelationId} subscription {SubscriptionReference}: {Error}",
            (int)exception.StatusCode, correlationId, reference, message);

        if (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            try
            {
                var found = await FindSubscriptionAsync(reference, cancellationToken);
                if (found != null && HandleMatches(found, planHandle))
                {
                    await CompleteAsync(claim, cancellationToken);
                    return new SubscribeResult { Subscription = MapSubscription(found), Created = false };
                }

                if (found == null)
                {
                    await ReleaseAsync(claim, cancellationToken);
                    throw new SubscriptionBillingException(message, 422, exception);
                }

                await MarkUnknownAsync(claim, cancellationToken);
                throw new BillingOutcomeUnknownException();
            }
            catch (SubscriptionBillingException)
            {
                throw;
            }
            catch (Exception)
            {
                await MarkUnknownAsync(claim, CancellationToken.None);
                throw new BillingOutcomeUnknownException();
            }
        }

        await ReleaseAsync(claim, cancellationToken);
        throw new SubscriptionBillingException(message, MapStatus((int)exception.StatusCode), exception);
    }

    private async Task<SubscribeResult> SettleSubscriptionAfterDisconnectAsync(
        BillingClaim claim,
        string reference,
        string planHandle,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var found = await FindSubscriptionAsync(reference, FreshSettleToken(cancellationToken));
            if (found != null && HandleMatches(found, planHandle))
            {
                await CompleteAsync(claim, cancellationToken);
                _logger.LogInformation(
                    "Billing CreateSubscription settled by FindSubscription correlation {CorrelationId} subscription {SubscriptionReference}",
                    correlationId, reference);
                return new SubscribeResult { Subscription = MapSubscription(found), Created = false };
            }

            await MarkUnknownAsync(claim, cancellationToken);
            throw new BillingOutcomeUnknownException();
        }
        catch (BillingOutcomeUnknownException)
        {
            throw;
        }
        catch (SubscriptionBillingException)
        {
            await MarkUnknownAsync(claim, CancellationToken.None);
            throw new BillingOutcomeUnknownException();
        }
        catch (Exception)
        {
            await MarkUnknownAsync(claim, CancellationToken.None);
            throw new BillingOutcomeUnknownException();
        }
    }

    private async Task<OfferedCatalog> LoadOfferedPlansAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        var plans = new List<OfferedPlan>();
        var truncated = false;
        var familyId = "handle:" + _settings.ProductFamilyHandle;

        for (var page = 1; page <= PageCap; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = familyId,
                        Page = page,
                        PerPage = PageSize,
                        IncludeArchived = false
                    },
                    cancellationToken: cancellationToken);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                var message = ex.StatusCode == HttpStatusCode.NotFound
                    ? "The configured product family was not found."
                    : "The billing catalog could not be read.";
                if (ex.Error.TryGetString(out var text) && !string.IsNullOrWhiteSpace(text))
                    message = text;
                _logger.LogError(
                    "Billing ListProductsForProductFamily failed with status {StatusCode} correlation {CorrelationId}",
                    (int)ex.StatusCode, correlationId);
                throw new SubscriptionBillingException(message, ex.StatusCode == HttpStatusCode.NotFound ? 502 : MapStatus((int)ex.StatusCode), ex);
            }

            foreach (var item in batch)
            {
                var product = item.Product;
                if (string.IsNullOrWhiteSpace(product.Handle))
                    continue;
                plans.Add(new OfferedPlan(product));
            }

            if (batch.Count < PageSize)
                break;

            if (page == PageCap)
                truncated = true;
        }

        _logger.LogInformation(
            "Billing ListProductsForProductFamily correlation {CorrelationId} count {PlanCount} truncated {Truncated}",
            correlationId, plans.Count, truncated);

        return new OfferedCatalog(plans, truncated);
    }

    private async Task<Customer> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await _client.Customers.ReadCustomerByReference(
            new ReadCustomerByReferenceRequest { Reference = reference },
            cancellationToken: cancellationToken);
        return response.Customer;
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: cancellationToken);
            return response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            _ = ex.Error.TryGetRawError(out _);
            throw new SubscriptionBillingException("The billing provider rejected the subscription lookup.", MapStatus((int)ex.StatusCode), ex);
        }
    }

    private async Task<ClaimAcquisition> TryClaimAsync(string reference, CancellationToken cancellationToken)
    {
        var tracked = _db.BillingClaims.Local.FirstOrDefault(row => row.Reference == reference);
        if (tracked != null)
            return new ClaimAcquisition(tracked, false);

        var claim = new BillingClaim(reference);
        try
        {
            _db.BillingClaims.Add(claim);
            await _db.SaveChangesAsync(cancellationToken);
            return new ClaimAcquisition(claim, true);
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            _db.ChangeTracker.Clear();
            var existing = await _db.BillingClaims.FirstOrDefaultAsync(row => row.Reference == reference, cancellationToken);
            if (existing == null)
                throw;
            return new ClaimAcquisition(existing, false);
        }
    }

    private async Task CompleteAsync(BillingClaim claim, CancellationToken cancellationToken)
    {
        claim.MarkCompleted();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkUnknownAsync(BillingClaim claim, CancellationToken cancellationToken)
    {
        if (_db.Entry(claim).State == EntityState.Detached)
            _db.BillingClaims.Attach(claim);
        claim.MarkUnknown();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ReleaseAsync(BillingClaim claim, CancellationToken cancellationToken)
    {
        if (_db.Entry(claim).State == EntityState.Detached)
            _db.BillingClaims.Attach(claim);
        _db.BillingClaims.Remove(claim);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private SubscriptionBillingException MapUnhandled(Exception exception, string operation, Guid correlationId)
    {
        switch (exception)
        {
            case ResponseDeserializationException deserialization:
                return Unreadable(deserialization, operation, correlationId);
            case SdkTimeoutException timeout:
                _logger.LogError(
                    "Billing {Operation} timed out correlation {CorrelationId}",
                    operation, correlationId);
                return new SubscriptionBillingException("The billing provider did not respond in time.", 504, timeout);
            case SdkConnectionException connection:
                _logger.LogError(
                    "Billing {Operation} could not reach the provider correlation {CorrelationId}",
                    operation, correlationId);
                return new SubscriptionBillingException("The billing provider could not be reached.", 502, connection);
            case AuthSchemeException auth:
                return Unavailable(operation, correlationId, auth);
            case ApiException<RawError> raw:
                _logger.LogError(
                    "Billing {Operation} failed with status {StatusCode} correlation {CorrelationId}",
                    operation, (int)raw.StatusCode, correlationId);
                return new SubscriptionBillingException("The billing provider rejected the request.", MapStatus((int)raw.StatusCode), raw);
            case ApiException api:
                _logger.LogError(
                    "Billing {Operation} failed with status {StatusCode} correlation {CorrelationId}",
                    operation, (int)api.StatusCode, correlationId);
                return new SubscriptionBillingException("The billing provider rejected the request.", MapStatus((int)api.StatusCode), api);
            default:
                _logger.LogError(
                    "Billing {Operation} failed correlation {CorrelationId}",
                    operation, correlationId);
                return new SubscriptionBillingException("The billing provider could not complete the request.", 502, exception);
        }
    }

    private SubscriptionBillingException Unreadable(ResponseDeserializationException exception, string operation, Guid correlationId)
    {
        var status = (int)exception.StatusCode;
        _logger.LogError(
            "Billing {Operation} returned a body that could not be processed. Status {StatusCode} correlation {CorrelationId}",
            operation, status, correlationId);
        var mapped = IsSuccess(exception.StatusCode) ? 502 : MapStatus(status);
        return new SubscriptionBillingException("The provider returned a response that could not be processed.", mapped, exception);
    }

    private SubscriptionBillingException Unavailable(string operation, Guid correlationId, AuthSchemeException exception)
    {
        _logger.LogError(
            "Billing {Operation} credentials could not be applied correlation {CorrelationId}",
            operation, correlationId);
        return new SubscriptionBillingException("The billing provider is unavailable.", 502, exception);
    }

    private static ShopperSubscription MapSubscription(Subscription? subscription)
    {
        if (subscription == null)
            throw new SubscriptionBillingException("The billing provider returned a subscription that could not be read.", 502);

        return new ShopperSubscription
        {
            Reference = subscription.Reference,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents,
            State = subscription.State?.Value,
            NextBillingAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt
        };
    }

    private static SubscriptionPlan ToPlan(OfferedPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    private static bool HandleMatches(Subscription subscription, string planHandle) =>
        string.Equals(subscription.Product?.Handle, planHandle, StringComparison.Ordinal);

    private static string Describe(Errors1? errors)
    {
        if (errors == null)
            return "The billing provider rejected the customer.";
        if (errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError.Customer))
            return customerError.Customer;
        if (errors.TryGetListOfString(out var list) && list.Count > 0)
            return string.Join(" ", list);
        return "The billing provider rejected the customer.";
    }

    private static int MapStatus(int status) => status switch
    {
        401 or 403 => 502,
        429 => 503,
        >= 400 and < 500 => status,
        _ => 502
    };

    private static bool IsSuccess(HttpStatusCode status)
    {
        var code = (int)status;
        return code >= 200 && code < 300;
    }

    private static bool IsDuplicateKey(Exception exception) =>
        exception is DbUpdateException
        || (exception is ArgumentException or InvalidOperationException
            && exception.Message.Contains("same key", StringComparison.OrdinalIgnoreCase));

    private static CancellationTokenSource Begin(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    private static CancellationToken FreshSettleToken(CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken;

    private sealed class ClaimAcquisition
    {
        public ClaimAcquisition(BillingClaim claim, bool inserted)
        {
            Claim = claim;
            Inserted = inserted;
        }

        public BillingClaim Claim { get; }
        public bool Inserted { get; }
    }

    private sealed class OfferedPlan
    {
        public OfferedPlan(Product product)
        {
            Handle = product.Handle ?? string.Empty;
            Name = product.Name;
            PriceInCents = product.PriceInCents;
            Interval = product.Interval;
            IntervalUnit = product.IntervalUnit?.Value;
            RequireCreditCard = product.RequireCreditCard;
        }

        public string Handle { get; }
        public string? Name { get; }
        public long? PriceInCents { get; }
        public int? Interval { get; }
        public string? IntervalUnit { get; }
        public bool? RequireCreditCard { get; }
    }

    private sealed class OfferedCatalog
    {
        public OfferedCatalog(IReadOnlyList<OfferedPlan> plans, bool truncated)
        {
            Plans = plans;
            Truncated = truncated;
            var byHandle = new Dictionary<string, OfferedPlan>(StringComparer.Ordinal);
            foreach (var plan in plans)
            {
                if (!byHandle.ContainsKey(plan.Handle))
                    byHandle.Add(plan.Handle, plan);
            }

            ByHandle = byHandle;
        }

        public IReadOnlyList<OfferedPlan> Plans { get; }
        public bool Truncated { get; }
        public IReadOnlyDictionary<string, OfferedPlan> ByHandle { get; }
    }
}
