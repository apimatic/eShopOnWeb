using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PageSize = 100;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new(StringComparer.Ordinal);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly MaxioWriteGuard _writeGuard;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        CatalogContext db,
        IOptions<MaxioSettings> settings,
        MaxioWriteGuard writeGuard)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _writeGuard = writeGuard;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            var familyResponses = await BoundedAsync(
                ct => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                cancellationToken);

            var families = familyResponses
                .Select(x => x.ProductFamily)
                .Where(x => x is not null && string.Equals(x.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal))
                .ToList();

            if (families.Count != 1 || families[0]!.Id is null)
            {
                throw new BillingProviderException("The configured Maxio product family could not be resolved.");
            }

            var products = new List<Product>();
            for (var page = 1; ; page++)
            {
                var pageItems = await BoundedAsync(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: families[0]!.Id!.Value.ToString(CultureInfo.InvariantCulture),
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: page,
                        perPage: PageSize,
                        ct: ct),
                    cancellationToken);

                products.AddRange(pageItems.Select(x => x.Product));
                if (pageItems.Count < PageSize)
                {
                    break;
                }
            }

            return products
                .Where(IsCompletePlan)
                .Select(x => new SubscriptionPlan(
                    x.Handle!,
                    x.Name!,
                    x.Description,
                    x.PriceInCents!.Value,
                    x.Interval!.Value,
                    x.IntervalUnit!.Value))
                .OrderBy(x => x.PriceInCents)
                .ToList();
        }
        catch (BillingProviderException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio plan discovery failed.", ex);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw new BillingProviderException("The configured Maxio product family is unavailable.", (int)HttpStatusCode.NotFound, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new BillingProviderException("The configured Maxio product family is unavailable.", (int)raw.StatusCode, ex);
            }

            throw new BillingProviderException("Maxio plan discovery failed.", null, ex);
        }
        catch (Exception ex) when (IsProviderProtocolOrTransportFailure(ex, cancellationToken))
        {
            throw new BillingProviderException("Maxio plan discovery is temporarily unavailable.", null, ex);
        }
    }

    public async Task<SubscriptionDetails> SubscribeAsync(BillingUser user, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingValidationException("A productHandle is required.");
        }

        productHandle = productHandle.Trim();
        var plans = await ListPlansAsync(cancellationToken);
        if (!plans.Any(x => string.Equals(x.ProductHandle, productHandle, StringComparison.Ordinal)))
        {
            throw new BillingValidationException("The requested subscription plan is not available.");
        }

        var lockKey = $"{user.Id}\n{productHandle}";
        var gate = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SubscribeInsideLockAsync(user, productHandle, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken)
    {
        var customerReference = CustomerReference(user.Id);
        var customer = await TryReadCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var customerId = RequireCustomer(customer, customerReference);
        await UpsertCustomerLinkAsync(user.Id, customerReference, customerId, cancellationToken);

        try
        {
            var responses = await BoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                cancellationToken);

            var result = new List<SubscriptionDetails>();
            foreach (var response in responses)
            {
                if (TryMapSubscription(response.Subscription, out var details))
                {
                    result.Add(details!);
                }
            }

            return result;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio subscriptions could not be loaded.", ex);
        }
        catch (Exception ex) when (IsProviderProtocolOrTransportFailure(ex, cancellationToken))
        {
            throw new BillingProviderException("Maxio subscriptions are temporarily unavailable.", null, ex);
        }
    }

    private async Task<SubscriptionDetails> SubscribeInsideLockAsync(BillingUser user, string productHandle, CancellationToken cancellationToken)
    {
        var reference = SubscriptionReference(user.Id, productHandle);
        var enrollment = await _db.SubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.ProductHandle == productHandle, cancellationToken);
        var ownsClaim = false;

        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment(user.Id, productHandle, reference);
            _db.SubscriptionEnrollments.Add(enrollment);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                ownsClaim = true;
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                enrollment = await _db.SubscriptionEnrollments
                    .SingleAsync(x => x.UserId == user.Id && x.ProductHandle == productHandle, cancellationToken);
            }
        }

        if (enrollment.Status == EnrollmentStatus.Rejected)
        {
            throw new BillingValidationException(enrollment.RejectionReason ?? "The subscription was rejected by Maxio.");
        }

        if (!ownsClaim)
        {
            if (enrollment.Status == EnrollmentStatus.Pending)
            {
                return Pending(enrollment.SubscriptionReference, productHandle);
            }

            var existing = await TryFindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
            if (existing is not null && TryMapSubscription(existing.Subscription, out var existingDetails))
            {
                if (existingDetails!.SubscriptionId is int existingId)
                {
                    enrollment.MarkSucceeded(existingId);
                    try
                    {
                        await _db.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        _db.ChangeTracker.Clear();
                    }
                }

                return existingDetails;
            }

            return Pending(enrollment.SubscriptionReference, productHandle);
        }

        var reconciled = await TryFindSubscriptionAsync(reference, cancellationToken);
        if (reconciled is not null && TryMapSubscription(reconciled.Subscription, out var reconciledDetails))
        {
            enrollment.MarkSucceeded(reconciledDetails!.SubscriptionId!.Value);
            await _db.SaveChangesAsync(cancellationToken);
            return reconciledDetails;
        }

        var customerId = await EnsureCustomerAsync(user, cancellationToken);
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = CollectionMethod.Remittance,
                Reference = reference
            }
        };

        try
        {
            SubscriptionResponse response;
            using (_writeGuard.Begin())
            {
                response = await BoundedAsync(
                    ct => _client.Subscriptions.CreateSubscription(body: request, ct: ct),
                    cancellationToken);
            }

            if (!TryMapSubscription(response.Subscription, out var created) ||
                created!.SubscriptionId is null ||
                !string.Equals(created.Reference, reference, StringComparison.Ordinal))
            {
                enrollment.MarkUnknown();
                await _db.SaveChangesAsync(cancellationToken);
                throw new BillingProviderException("Maxio returned an incomplete subscription response.");
            }

            enrollment.MarkSucceeded(created.SubscriptionId.Value);
            await _db.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                var reason = errors.Errors.Count > 0 ? string.Join(" ", errors.Errors) : "The subscription was rejected by Maxio.";
                enrollment.MarkRejected(Truncate(reason, 500));
                await _db.SaveChangesAsync(cancellationToken);
                throw new BillingValidationException(reason);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                enrollment.MarkRejected("The subscription was rejected by Maxio.");
                await _db.SaveChangesAsync(cancellationToken);
                throw FromRawError(raw, "The subscription was rejected by Maxio.", ex);
            }

            enrollment.MarkUnknown();
            await _db.SaveChangesAsync(cancellationToken);
            throw new BillingProviderException("The Maxio subscription outcome is unknown.", null, ex);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex, cancellationToken))
        {
            var found = await TryFindSubscriptionAsync(reference, cancellationToken);
            if (found is not null && TryMapSubscription(found.Subscription, out var recovered) && recovered!.SubscriptionId is int recoveredId)
            {
                enrollment.MarkSucceeded(recoveredId);
                await _db.SaveChangesAsync(cancellationToken);
                return recovered;
            }

            enrollment.MarkUnknown();
            await _db.SaveChangesAsync(cancellationToken);
            return Pending(reference, productHandle);
        }
    }

    private async Task<int> EnsureCustomerAsync(BillingUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existing = await TryReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            var existingId = RequireCustomer(existing, reference);
            await UpsertCustomerLinkAsync(user.Id, reference, existingId, cancellationToken);
            return existingId;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Reference = reference
            }
        };

        try
        {
            CustomerResponse response;
            using (_writeGuard.Begin())
            {
                response = await BoundedAsync(
                    ct => _client.Customers.CreateCustomer(body: request, ct: ct),
                    cancellationToken);
            }

            var customerId = RequireCustomer(response, reference);
            await UpsertCustomerLinkAsync(user.Id, reference, customerId, cancellationToken);
            return customerId;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _) || ex.Error.TryGetRawError(out _))
            {
                var raced = await TryReadCustomerAsync(reference, cancellationToken);
                if (raced is not null)
                {
                    var racedId = RequireCustomer(raced, reference);
                    await UpsertCustomerLinkAsync(user.Id, reference, racedId, cancellationToken);
                    return racedId;
                }
            }

            throw new BillingValidationException("Maxio rejected the customer profile.");
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex, cancellationToken))
        {
            var reconciled = await TryReadCustomerAsync(reference, cancellationToken);
            if (reconciled is not null)
            {
                var reconciledId = RequireCustomer(reconciled, reference);
                await UpsertCustomerLinkAsync(user.Id, reference, reconciledId, cancellationToken);
                return reconciledId;
            }

            throw new BillingProviderException("The Maxio customer outcome is unknown.", null, ex);
        }
    }

    private async Task<CustomerResponse?> TryReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(reference: reference, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio customer lookup failed.", ex);
        }
        catch (Exception ex) when (IsProviderProtocolOrTransportFailure(ex, cancellationToken))
        {
            throw new BillingProviderException("Maxio customer lookup is temporarily unavailable.", null, ex);
        }
    }

    private async Task<SubscriptionResponse?> TryFindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedAsync(
                ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct),
                cancellationToken);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio subscription lookup failed.", ex);
            }

            throw new BillingProviderException("Maxio subscription lookup failed.", null, ex);
        }
        catch (Exception ex) when (IsProviderProtocolOrTransportFailure(ex, cancellationToken))
        {
            throw new BillingProviderException("Maxio subscription lookup is temporarily unavailable.", null, ex);
        }
    }

    private async Task UpsertCustomerLinkAsync(string userId, string reference, int customerId, CancellationToken cancellationToken)
    {
        var link = await _db.MaxioCustomerLinks.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (link is null)
        {
            _db.MaxioCustomerLinks.Add(new MaxioCustomerLink(userId, reference, customerId));
        }
        else
        {
            link.Refresh(customerId);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
        }
    }

    private static int RequireCustomer(CustomerResponse response, string reference)
    {
        var customer = response.Customer;
        if (customer is null || customer.Id is null || !string.Equals(customer.Reference, reference, StringComparison.Ordinal))
        {
            throw new BillingProviderException("Maxio returned an incomplete customer response.");
        }

        return customer.Id.Value;
    }

    private static bool TryMapSubscription(Subscription? subscription, out SubscriptionDetails? result)
    {
        var product = subscription?.Product;
        if (subscription?.Id is null ||
            string.IsNullOrWhiteSpace(subscription.Reference) ||
            product is null ||
            string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name) ||
            subscription.State is null)
        {
            result = null;
            return false;
        }

        result = new SubscriptionDetails(
            subscription.Id.Value,
            subscription.Reference,
            product.Handle,
            product.Name,
            subscription.ProductPriceInCents,
            subscription.Currency,
            subscription.State.Value,
            subscription.CurrentPeriodEndsAt,
            subscription.NextAssessmentAt);
        return true;
    }

    private static bool IsCompletePlan(Product product) =>
        product.ArchivedAt is null &&
        !string.IsNullOrWhiteSpace(product.Handle) &&
        !string.IsNullOrWhiteSpace(product.Name) &&
        product.PriceInCents is not null &&
        product.Interval is not null &&
        product.IntervalUnit is not null;

    private static SubscriptionDetails Pending(string reference, string productHandle) =>
        new(null, reference, productHandle, productHandle, null, null, "processing", null, null, true);

    private static string CustomerReference(string userId) => "eshop-c-" + Hash(userId);
    private static string SubscriptionReference(string userId, string productHandle) => "eshop-s-" + Hash($"{userId}|{productHandle}");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..32];
    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static BillingProviderException FromRawError(RawError error, string message, Exception innerException) =>
        new(message, (int)error.StatusCode, innerException);

    private static bool IsProviderProtocolOrTransportFailure(Exception exception, CancellationToken callerToken) =>
        exception is HttpRequestException or JsonException or MaxioWriteReplayBlockedException ||
        (exception is TaskCanceledException && !callerToken.IsCancellationRequested);

    private static bool IsAmbiguousWriteFailure(Exception exception, CancellationToken callerToken) =>
        IsProviderProtocolOrTransportFailure(exception, callerToken);

    private static async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(25));
        return await call(budget.Token);
    }
}
