using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService
{
    private const int MaxProductsPerPage = 200;
    private static readonly TimeSpan ProviderCallBudget = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscriptionLocks = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly AppIdentityDbContext _identityDb;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        AppIdentityDbContext identityDb,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioOptions> options)
    {
        _client = client;
        _identityDb = identityDb;
        _userManager = userManager;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        var plans = new List<SubscriptionPlanDto>();
        var page = 1;

        while (true)
        {
            var products = await WithProviderBudgetAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: ProductFamilyReference(),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: page,
                perPage: MaxProductsPerPage,
                ct: ct), cancellationToken);

            foreach (var productResponse in products)
            {
                var product = productResponse.Product;
                if (!string.IsNullOrWhiteSpace(product.Handle))
                {
                    plans.Add(new SubscriptionPlanDto
                    {
                        Handle = product.Handle,
                        Name = product.Name ?? product.Handle,
                        PriceInCents = product.PriceInCents,
                        Interval = product.Interval,
                        IntervalUnit = product.IntervalUnit?.Value,
                        PricePointHandle = product.ProductPricePointHandle
                    });
                }
            }

            if (products.Count < MaxProductsPerPage)
                break;

            page++;
        }

        return plans;
    }

    public async Task<(SubscriptionDto Subscription, bool Created)> SubscribeAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            throw new MaxioBillingException(HttpStatusCode.BadRequest, "PlanHandle is required.");

        var identity = await GetIdentityAsync(principal, cancellationToken);
        var plan = (await ListPlansAsync(cancellationToken))
            .FirstOrDefault(candidate => string.Equals(candidate.Handle, request.PlanHandle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (plan is null)
            throw new MaxioBillingException(HttpStatusCode.BadRequest, "The requested subscription plan was not found.");

        var idempotencyMaterial = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? plan.Handle
            : request.IdempotencyKey.Trim();
        var subscriptionReference = CreateReference(identity.CustomerReference, plan.Handle, idempotencyMaterial);
        var subscriptionLock = SubscriptionLocks.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));

        await subscriptionLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
            {
                await SaveIdempotencyRecordAsync(identity.CustomerReference, subscriptionReference, existing.Id, cancellationToken);
                return (MapSubscription(existing), false);
            }

            await SaveIdempotencyRecordAsync(identity.CustomerReference, subscriptionReference, null, cancellationToken);
            var customer = await EnsureCustomerAsync(identity, cancellationToken);
            var pricePointHandle = request.ProductPricePointHandle ?? plan.PricePointHandle;
            var body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    ProductPricePointHandle = pricePointHandle,
                    CustomerReference = identity.CustomerReference,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice
                }
            };

            var subscription = await CreateSubscriptionWithRecoveryAsync(body, subscriptionReference, cancellationToken);
            await SaveIdempotencyRecordAsync(identity.CustomerReference, subscriptionReference, subscription.Id, cancellationToken);
            return (MapSubscription(subscription), true);
        }
        finally
        {
            subscriptionLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        var identity = await GetIdentityAsync(principal, cancellationToken);
        var customer = await EnsureCustomerAsync(identity, cancellationToken);
        if (!customer.Id.HasValue)
            throw new MaxioBillingException(HttpStatusCode.BadGateway, "Maxio returned a customer without an ID.");

        var subscriptions = await WithProviderBudgetAsync(ct =>
            _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct), cancellationToken);
        return subscriptions
            .Where(response => response.Subscription is not null)
            .Select(response => MapSubscription(response.Subscription!))
            .ToArray();
    }

    private async Task<CustomerIdentity> GetIdentityAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var tokenIdentity = principal.Identity?.Name
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(tokenIdentity))
            throw new MaxioBillingException(HttpStatusCode.Unauthorized, "The access token has no usable identity.");

        var normalizedIdentity = tokenIdentity.Trim().ToLowerInvariant();
        var user = await _userManager.FindByNameAsync(tokenIdentity.Trim());
        var email = user?.Email ?? tokenIdentity.Trim();
        var (firstName, lastName) = NameParts(email);
        return new CustomerIdentity(
            CreateReference("user", normalizedIdentity),
            email,
            firstName,
            lastName);
    }

    private async Task<Customer> EnsureCustomerAsync(CustomerIdentity identity, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadCustomerAsync(identity.CustomerReference, cancellationToken);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The reference is unique in Maxio. If another request wins creation, the
            // follow-up lookup below makes the operation idempotent.
            try
            {
                var created = await WithProviderBudgetAsync(ct => _client.Customers.CreateCustomer(
                    new MaxioAdvancedBilling.Models.CreateCustomerRequest
                    {
                        Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                        {
                            FirstName = identity.FirstName,
                            LastName = identity.LastName,
                            Email = identity.Email,
                            Reference = identity.CustomerReference
                        }
                    }, ct: ct), cancellationToken);
                return RequireCustomer(created.Customer);
            }
            catch (SdkException<CreateCustomerError>)
            {
                return await ReadCustomerAsync(identity.CustomerReference, cancellationToken);
            }
        }
    }

    private async Task<Customer> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await WithProviderBudgetAsync(ct =>
                _client.Customers.ReadCustomerByReference(reference, ct: ct), cancellationToken);
            return RequireCustomer(response.Customer);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioBillingException(HttpStatusCode.NotFound, "Maxio customer was not found.", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("Maxio customer lookup failed.", ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await WithProviderBudgetAsync(ct =>
                _client.Subscriptions.FindSubscription(reference, ct: ct), cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex)
            when (ex.Error.TryGetNoContent(out RawError _))
        {
            return null;
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex)
        {
            throw ProviderFailure("Maxio subscription lookup failed.", ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionWithRecoveryAsync(
        MaxioAdvancedBilling.Models.CreateSubscriptionRequest body,
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await WithProviderBudgetAsync(ct =>
                _client.Subscriptions.CreateSubscription(body, ct: ct), cancellationToken);
            return RequireSubscription(response.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var recovered = await FindSubscriptionAsync(reference, cancellationToken);
            if (recovered is not null)
                return recovered;

            var statusCode = HttpStatusCode.BadGateway;
            var reason = "Maxio subscription creation was rejected.";
            if (ex.Error.TryGetErrorListResponse1(out ErrorListResponse1 details) && details.Errors.Count > 0)
            {
                statusCode = HttpStatusCode.UnprocessableEntity;
                reason = $"Maxio subscription creation was rejected: {string.Join("; ", details.Errors)}";
            }
            else if (ex.Error.TryGetRawError(out RawError raw))
            {
                var rawMessage = raw.ReadAsString();
                if (!string.IsNullOrWhiteSpace(rawMessage))
                    reason = $"Maxio subscription creation was rejected: {rawMessage}";
            }

            throw new MaxioBillingException(statusCode, reason, ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A transport failure can happen after Maxio accepted the write. Reconcile
            // by reference before reporting failure; never blindly resend the write.
            var recovered = await FindSubscriptionAsync(reference, cancellationToken);
            if (recovered is not null)
                return recovered;
            throw new MaxioBillingException(HttpStatusCode.BadGateway, "Maxio subscription creation outcome is unknown.", ex);
        }
    }

    private async Task SaveIdempotencyRecordAsync(
        string customerReference,
        string subscriptionReference,
        int? maxioSubscriptionId,
        CancellationToken cancellationToken)
    {
        var record = await _identityDb.SubscriptionIdempotencyRecords
            .SingleOrDefaultAsync(item => item.Key == subscriptionReference, cancellationToken);
        if (record is null)
        {
            record = new SubscriptionIdempotencyRecord
            {
                Key = subscriptionReference,
                CustomerReference = customerReference,
                SubscriptionReference = subscriptionReference,
                MaxioSubscriptionId = maxioSubscriptionId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _identityDb.SubscriptionIdempotencyRecords.Add(record);
        }
        else
        {
            record.MaxioSubscriptionId = maxioSubscriptionId ?? record.MaxioSubscriptionId;
            record.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionDto MapSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            PlanHandle = product?.Handle,
            PlanName = product?.Name,
            PriceInCents = subscription.CurrentBillingAmountInCents
                ?? subscription.ProductPriceInCents
                ?? product?.PriceInCents,
            State = subscription.State?.Value,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
        };
    }

    private static Customer RequireCustomer(Customer? customer) => customer
        ?? throw new MaxioBillingException(HttpStatusCode.BadGateway, "Maxio returned an empty customer response.");

    private static Subscription RequireSubscription(Subscription? subscription) => subscription
        ?? throw new MaxioBillingException(HttpStatusCode.BadGateway, "Maxio returned an empty subscription response.");

    private static MaxioBillingException ProviderFailure(string message, Exception innerException) =>
        new(HttpStatusCode.BadGateway, message, innerException);

    private async Task<T> WithProviderBudgetAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(ProviderCallBudget);
        return await operation(budget.Token);
    }

    private void ValidateConfiguration() => _options.Validate();

    private string ProductFamilyReference()
    {
        var handle = _options.ProductFamilyHandle!.Trim();
        return handle.StartsWith("handle:", StringComparison.OrdinalIgnoreCase)
            ? $"handle:{handle["handle:".Length..]}"
            : $"handle:{handle}";
    }

    private static (string FirstName, string LastName) NameParts(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var parts = localPart.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? parts[0] : "eShop";
        var last = parts.Length > 1 ? parts[^1] : "Shopper";
        return (Capitalize(first), Capitalize(last));
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? "Shopper" : char.ToUpperInvariant(value[0]) + value[1..];

    private static string CreateReference(string prefix, string identity, string? detail = null)
    {
        var material = detail is null ? identity : $"{identity}\n{detail}";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return $"eshop-{prefix}-{digest[..32]}";
    }

    private sealed record CustomerIdentity(string CustomerReference, string Email, string FirstName, string LastName);
}
