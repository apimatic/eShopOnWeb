using System;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingService(
    MaxioAdvancedBillingClient client,
    AppIdentityDbContext identityDbContext,
    UserManager<ApplicationUser> userManager,
    IOptions<MaxioOptions> options,
    SubscriptionOperationLock operationLock,
    MaxioCallContext callContext,
    ILogger<SubscriptionBillingService> logger) : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int CatalogPageSize = 100;
    private const string CustomerReady = "Ready";
    private const string IntentCreating = "Creating";
    private const string IntentCreated = "Created";
    private const string IntentNeedsReconciliation = "NeedsReconciliation";
    private readonly MaxioOptions _options = options.Value;

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var families = await ExecuteRawAsync(
            ct => client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct),
            cancellationToken);

        var family = families
            .Select(x => x.ProductFamily)
            .SingleOrDefault(x => x is not null &&
                x.ArchivedAt is null &&
                string.Equals(x.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));

        if (family?.Id is null)
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.NotFound,
                $"The configured subscription product family '{_options.ProductFamilyHandle}' was not found.");
        }

        var plans = new List<SubscriptionPlanDto>();
        for (var page = 1; ; page++)
        {
            var responsePage = await ListProductsAsync(
                RequireProviderInt32(family.Id.Value, "product_family.id"),
                page,
                cancellationToken);
            plans.AddRange(responsePage
                .Select(x => x.Product)
                .Where(x => x.ArchivedAt is null && !string.IsNullOrWhiteSpace(x.Handle))
                .Select(x => new SubscriptionPlanDto(
                    x.Handle!,
                    x.Name,
                    x.PriceInCents,
                    ProviderInt32OrNull(x.Interval, "product.interval"),
                    x.IntervalUnit?.Value,
                    null)));

            if (responsePage.Count < CatalogPageSize)
            {
                break;
            }
        }

        return plans.OrderBy(x => x.PriceInCents).ThenBy(x => x.Handle, StringComparer.Ordinal).ToList();
    }

    public async Task<CreateSubscriptionResponse> SubscribeAsync(
        string username,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadRequest, "ProductHandle is required.");
        }

        productHandle = productHandle.Trim();
        var user = await ResolveUserAsync(username);

        using (await operationLock.AcquireAsync($"subscription:{user.Id}:{productHandle}", cancellationToken))
        {
            await ValidateProductAsync(productHandle, cancellationToken);

            MaxioCustomerLink customerLink;
            using (await operationLock.AcquireAsync($"customer:{user.Id}", cancellationToken))
            {
                customerLink = await EnsureCustomerAsync(user, cancellationToken);
            }

            var intent = await GetOrCreateIntentAsync(user.Id, customerLink.CustomerReference, productHandle, cancellationToken);

            var existing = await FindSubscriptionAsync(intent.SubscriptionReference, cancellationToken);
            if (existing is not null)
            {
                await MarkCreatedAsync(intent, ProviderInt32OrNull(existing.Id, "subscription.id"), cancellationToken);
                return new CreateSubscriptionResponse(MapSubscription(existing), false);
            }

            if (string.Equals(intent.Status, IntentCreated, StringComparison.Ordinal))
            {
                throw new SubscriptionBillingException(
                    HttpStatusCode.Conflict,
                    "The local subscription is marked as created but Maxio could not find it; reconciliation is required.");
            }

            try
            {
                var created = await CreateSubscriptionAsync(
                    productHandle,
                    customerLink.CustomerReference,
                    intent.SubscriptionReference,
                    cancellationToken);

                if (created is null)
                {
                    throw new SubscriptionBillingException(
                        HttpStatusCode.UnprocessableEntity,
                        "Maxio's response did not contain the optional subscription field required to confirm enrollment.");
                }

                await MarkCreatedAsync(intent, ProviderInt32OrNull(created.Id, "subscription.id"), cancellationToken);
                return new CreateSubscriptionResponse(MapSubscription(created), true);
            }
            catch (MaxioWriteRetryBlockedException ex)
            {
                await MarkNeedsReconciliationAsync(intent, null, "TransportOutcomeUnknown", cancellationToken);
                var reconciled = await FindSubscriptionAsync(intent.SubscriptionReference, cancellationToken);
                if (reconciled is not null)
                {
                    await MarkCreatedAsync(intent, ProviderInt32OrNull(reconciled.Id, "subscription.id"), cancellationToken);
                    return new CreateSubscriptionResponse(MapSubscription(reconciled), false);
                }

                throw new SubscriptionBillingException(
                    HttpStatusCode.BadGateway,
                    "Maxio did not confirm whether the subscription was created. Retry safely to reconcile the same intent.",
                    ex);
            }
            catch (SubscriptionBillingException ex) when ((int)ex.StatusCode >= 500)
            {
                await MarkNeedsReconciliationAsync(intent, (int)ex.StatusCode, "ProviderFailure", cancellationToken);
                throw;
            }
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        string username,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(username);
        var customerLink = await identityDbContext.MaxioCustomerLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);

        if (customerLink?.MaxioCustomerId is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var responses = await ExecuteRawAsync(
            ct => client.Customers.ListCustomerSubscriptions(customerLink.MaxioCustomerId.Value, ct: ct),
            cancellationToken);

        return responses
            .Where(x => x.Subscription is not null)
            .Select(x => MapSubscription(x.Subscription!))
            .OrderBy(x => x.ProductName, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(
        int familyId,
        int page,
        CancellationToken cancellationToken)
    {
        using var contextScope = callContext.Begin();
        using var cts = CreateCallToken(cancellationToken);
        try
        {
            return await client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: page,
                perPage: CatalogPageSize,
                ct: cts.Token);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw ProviderFailure(HttpStatusCode.NotFound, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(raw.StatusCode, ex);
            }
            throw ProviderFailure(HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundaryException(ex, cancellationToken);
        }
    }

    private async Task ValidateProductAsync(string productHandle, CancellationToken cancellationToken)
    {
        var response = await ExecuteRawAsync(
            ct => client.Products.ReadProductByHandle(productHandle, ct: ct),
            cancellationToken);

        if (!string.Equals(response.Product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.NotFound,
                $"Subscription plan '{productHandle}' is not available in the configured product family.");
        }

        if (response.Product.ArchivedAt is not null)
        {
            throw new SubscriptionBillingException(HttpStatusCode.Conflict, $"Subscription plan '{productHandle}' is archived.");
        }
    }

    private async Task<MaxioCustomerLink> EnsureCustomerAsync(BillingUser user, CancellationToken cancellationToken)
    {
        var customerLink = await identityDbContext.MaxioCustomerLinks
            .SingleOrDefaultAsync(x => x.UserId == user.Id, cancellationToken);

        if (customerLink?.MaxioCustomerId is not null)
        {
            return customerLink;
        }

        if (customerLink is null)
        {
            var now = DateTimeOffset.UtcNow;
            customerLink = new MaxioCustomerLink
            {
                UserId = user.Id,
                CustomerReference = CreateCustomerReference(user.Id),
                Status = "Creating",
                CreatedAt = now,
                UpdatedAt = now
            };
            identityDbContext.MaxioCustomerLinks.Add(customerLink);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }

        var providerCustomer = await ReadCustomerAsync(customerLink.CustomerReference, cancellationToken);
        if (providerCustomer is null)
        {
            try
            {
                providerCustomer = await CreateCustomerAsync(user, customerLink.CustomerReference, cancellationToken);
            }
            catch (MaxioWriteRetryBlockedException ex)
            {
                providerCustomer = await ReadCustomerAsync(customerLink.CustomerReference, cancellationToken);
                if (providerCustomer is null)
                {
                    throw new SubscriptionBillingException(
                        HttpStatusCode.BadGateway,
                        "Maxio did not confirm whether the customer was created. Retry safely to reconcile the same customer.",
                        ex);
                }
            }
        }

        if (providerCustomer.Id is null)
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.UnprocessableEntity,
                "Maxio's customer response did not contain the optional id field required for subscription enrollment.");
        }
        if (!string.Equals(providerCustomer.Reference, customerLink.CustomerReference, StringComparison.Ordinal))
        {
            throw new SubscriptionBillingException(HttpStatusCode.Conflict, "Maxio returned a customer with an unexpected reference.");
        }

        customerLink.MaxioCustomerId = RequireProviderInt32(providerCustomer.Id.Value, "customer.id");
        customerLink.Status = CustomerReady;
        customerLink.UpdatedAt = DateTimeOffset.UtcNow;
        await identityDbContext.SaveChangesAsync(cancellationToken);
        return customerLink;
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteRawAsync(
                ct => client.Customers.ReadCustomerByReference(reference, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SubscriptionBillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Customer> CreateCustomerAsync(
        BillingUser user,
        string reference,
        CancellationToken cancellationToken)
    {
        using var contextScope = callContext.Begin(atMostOneWrite: true);
        using var cts = CreateCallToken(cancellationToken);
        try
        {
            var response = await client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = user.FirstName,
                        LastName = user.LastName,
                        Email = user.Email,
                        Reference = reference
                    }
                },
                ct: cts.Token);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var reconciled = await ReadCustomerAsync(reference, cancellationToken);
                if (reconciled is not null && string.Equals(reconciled.Reference, reference, StringComparison.Ordinal))
                {
                    return reconciled;
                }
                throw ProviderFailure(HttpStatusCode.UnprocessableEntity, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(raw.StatusCode, ex);
            }
            throw ProviderFailure(HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundaryException(ex, cancellationToken);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        using var contextScope = callContext.Begin();
        using var cts = CreateCallToken(cancellationToken);
        try
        {
            var response = await client.Subscriptions.FindSubscription(reference: reference, ct: cts.Token);
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
                throw ProviderFailure(raw.StatusCode, ex);
            }
            throw ProviderFailure(HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundaryException(ex, cancellationToken);
        }
    }

    private async Task<Subscription?> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        using var contextScope = callContext.Begin(atMostOneWrite: true);
        using var cts = CreateCallToken(cancellationToken);
        try
        {
            var response = await client.Subscriptions.CreateSubscription(
                new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                {
                    Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerReference = customerReference,
                        Reference = subscriptionReference
                    }
                },
                ct: cts.Token);
            return response.Subscription;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var safeDetail = string.Join(" ", errorList.Errors.Take(5));
                throw new SubscriptionBillingException(
                    HttpStatusCode.UnprocessableEntity,
                    string.IsNullOrWhiteSpace(safeDetail) ? "Maxio rejected the subscription request." : safeDetail,
                    ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ProviderFailure(raw.StatusCode, ex);
            }
            throw ProviderFailure(HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundaryException(ex, cancellationToken);
        }
    }

    private async Task<MaxioSubscriptionIntent> GetOrCreateIntentAsync(
        string userId,
        string customerReference,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var intent = await identityDbContext.MaxioSubscriptionIntents
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        if (intent is not null)
        {
            return intent;
        }

        var now = DateTimeOffset.UtcNow;
        intent = new MaxioSubscriptionIntent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProductHandle = productHandle,
            CustomerReference = customerReference,
            SubscriptionReference = string.Empty,
            Status = IntentCreating,
            CreatedAt = now,
            UpdatedAt = now
        };
        intent.SubscriptionReference = $"eshop-sub:{intent.Id:N}";
        identityDbContext.MaxioSubscriptionIntents.Add(intent);
        await identityDbContext.SaveChangesAsync(cancellationToken);
        return intent;
    }

    private async Task MarkCreatedAsync(MaxioSubscriptionIntent intent, int? subscriptionId, CancellationToken cancellationToken)
    {
        intent.MaxioSubscriptionId = subscriptionId;
        intent.Status = IntentCreated;
        intent.LastProviderStatusCode = null;
        intent.LastErrorCategory = null;
        intent.UpdatedAt = DateTimeOffset.UtcNow;
        await identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkNeedsReconciliationAsync(
        MaxioSubscriptionIntent intent,
        int? statusCode,
        string category,
        CancellationToken cancellationToken)
    {
        intent.Status = IntentNeedsReconciliation;
        intent.LastProviderStatusCode = statusCode;
        intent.LastErrorCategory = category;
        intent.UpdatedAt = DateTimeOffset.UtcNow;
        await identityDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<BillingUser> ResolveUserAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new SubscriptionBillingException(HttpStatusCode.Unauthorized, "The access token does not identify a user.");
        }

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            throw new SubscriptionBillingException(HttpStatusCode.Unauthorized, "The access token user no longer exists.");
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new SubscriptionBillingException(HttpStatusCode.UnprocessableEntity, "The local user profile does not contain an email address.");
        }

        var localPart = email.Split('@', 2)[0].Trim();
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        return new BillingUser(user.Id, email, firstName, "Customer");
    }

    private async Task<T> ExecuteRawAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var contextScope = callContext.Begin();
        using var cts = CreateCallToken(cancellationToken);
        try
        {
            return await action(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex))
        {
            throw TranslateBoundaryException(ex, cancellationToken);
        }
    }

    private CancellationTokenSource CreateCallToken(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    private SubscriptionBillingException TranslateBoundaryException(Exception exception, CancellationToken callerToken)
    {
        if (exception is OperationCanceledException && callerToken.IsCancellationRequested)
        {
            throw exception;
        }
        if (exception is MaxioWriteRetryBlockedException blocked)
        {
            throw blocked;
        }
        if (exception is TaskCanceledException)
        {
            return new SubscriptionBillingException(HttpStatusCode.GatewayTimeout, "The Maxio request timed out.", exception);
        }
        if (exception is HttpRequestException)
        {
            return new SubscriptionBillingException(HttpStatusCode.ServiceUnavailable, "Maxio is temporarily unreachable.", exception);
        }
        if (exception is JsonException)
        {
            var status = callContext.LastStatusCode;
            if (status is not null && (int)status.Value is >= 400 and < 500)
            {
                return new SubscriptionBillingException(status.Value, "Maxio rejected the request, but its error response could not be processed.", exception);
            }
            return new SubscriptionBillingException(HttpStatusCode.BadGateway, "Maxio returned a response that could not be processed.", exception);
        }

        return new SubscriptionBillingException(HttpStatusCode.BadGateway, "The Maxio request failed.", exception);
    }

    private static bool IsBoundaryException(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException or MaxioWriteRetryBlockedException;

    private SubscriptionBillingException ProviderFailure(HttpStatusCode statusCode, Exception exception)
    {
        logger.LogWarning(exception, "Maxio request failed with HTTP status {StatusCode}.", (int)statusCode);
        var outwardStatus = (int)statusCode is >= 400 and < 500 ? statusCode : HttpStatusCode.BadGateway;
        return new SubscriptionBillingException(outwardStatus, "Maxio rejected the billing request.", exception);
    }

    private static string CreateCustomerReference(string userId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userId));
        return $"eshop:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static int? ProviderInt32OrNull(double? value, string fieldName) =>
        value is null ? null : RequireProviderInt32(value.Value, fieldName);

    private static int RequireProviderInt32(double value, string fieldName)
    {
        if (double.IsNaN(value) ||
            double.IsInfinity(value) ||
            value != Math.Truncate(value) ||
            value < int.MinValue ||
            value > int.MaxValue)
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.BadGateway,
                $"Maxio returned an invalid integral value for '{fieldName}'.");
        }

        return checked((int)value);
    }

    private static long? ProviderInt64OrNull(double? value, string fieldName)
    {
        if (value is null)
        {
            return null;
        }

        if (double.IsNaN(value.Value) ||
            double.IsInfinity(value.Value) ||
            value.Value != Math.Truncate(value.Value) ||
            value.Value < long.MinValue ||
            value.Value > long.MaxValue)
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.BadGateway,
                $"Maxio returned an invalid integral value for '{fieldName}'.");
        }

        return checked((long)value.Value);
    }

    private static SubscriptionDto MapSubscription(Subscription subscription) => new(
        ProviderInt64OrNull(subscription.Id, "subscription.id"),
        subscription.Reference,
        subscription.Product?.Handle,
        subscription.Product?.Name,
        subscription.ProductPriceInCents,
        subscription.CurrentBillingAmountInCents,
        subscription.Currency,
        subscription.State?.Value,
        subscription.NextAssessmentAt,
        subscription.CurrentPeriodEndsAt);
}
