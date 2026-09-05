using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
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

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// The application boundary around Maxio. It deliberately owns provider references,
/// retries reconciliation, and public error translation rather than exposing SDK types to endpoints.
/// </summary>
public sealed class SubscriptionBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentGates = new();
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly AppIdentityDbContext _identityDb;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriptionBillingService> _logger;
    private static readonly TimeSpan ProviderCallBudget = TimeSpan.FromSeconds(30);

    public SubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        Microsoft.Extensions.Options.IOptions<MaxioOptions> options,
        AppIdentityDbContext identityDb,
        UserManager<ApplicationUser> userManager,
        ILogger<SubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _identityDb = identityDb;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var family = await GetConfiguredFamilyAsync(cancellationToken);
        var products = new List<Product>();
        const int pageSize = 100;

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> response;
            try
            {
                response = await BoundedAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: family.Id!.Value.ToString(),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: pageSize,
                    ct: ct), cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> error)
            {
                if (error.Error.TryGetString(out _))
                {
                    throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway,
                        "The configured Maxio product family could not be loaded.", error);
                }

                throw ProviderFailure(error);
            }

            products.AddRange(response
                .Select(item => item.Product)
                .Where(product => product is not null)
                .Select(product => product!));

            if (response.Count < pageSize)
            {
                break;
            }
        }

        return products
            .Where(product => product.ArchivedAt is null)
            .Where(product => string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle,
                StringComparison.OrdinalIgnoreCase))
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .Select(MapPlan)
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ClaimsPrincipal principal,
        string requestedPlanHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestedPlanHandle))
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.BadRequest, "A plan handle is required.");
        }

        var user = await GetUserAsync(principal);
        var plan = (await GetPlansAsync(cancellationToken)).SingleOrDefault(candidate =>
            string.Equals(candidate.Handle, requestedPlanHandle.Trim(), StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.NotFound, "The requested subscription plan was not found.");
        }

        var gate = EnrollmentGates.GetOrAdd($"{user.Id}\n{plan.Handle}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var record = await GetOrCreateRecordAsync(user.Id, plan.Handle, cancellationToken);
            var customer = await EnsureCustomerAsync(user, cancellationToken);

            var subscription = await FindSubscriptionAsync(record.SubscriptionReference, cancellationToken);
            if (subscription is null)
            {
                subscription = await CreateSubscriptionAndReconcileAsync(customer, plan, record.SubscriptionReference,
                    cancellationToken);
            }

            EnsureSubscriptionOwnership(subscription, customer, plan.Handle);
            record.MaxioSubscriptionId = subscription.Id;
            await _identityDb.SaveChangesAsync(cancellationToken);

            return MapSubscription(subscription);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var customer = await FindCustomerAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null || customer.Id is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await BoundedAsync(ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct),
            cancellationToken);

        return subscriptions
            .Select(response => response.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => MapSubscription(subscription!))
            .OrderBy(subscription => subscription.NextBillingDate)
            .ToList();
    }

    private async Task<ProductFamily> GetConfiguredFamilyAsync(CancellationToken cancellationToken)
    {
        var families = await BoundedAsync(ct => _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: ct), cancellationToken);

        var family = families
            .Select(response => response.ProductFamily)
            .SingleOrDefault(candidate => candidate is not null &&
                string.Equals(candidate.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is null)
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway,
                "The configured Maxio product family was not found.");
        }

        return family;
    }

    private async Task<ApplicationUser> GetUserAsync(ClaimsPrincipal principal)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.Unauthorized, "An authenticated user is required.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.Unauthorized,
                "The authenticated user could not be resolved for billing.");
        }

        return user;
    }

    private async Task<MaxioSubscriptionRecord> GetOrCreateRecordAsync(
        string userId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var existing = await _identityDb.MaxioSubscriptionRecords.SingleOrDefaultAsync(record =>
            record.UserId == userId && record.PlanHandle == planHandle, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var record = new MaxioSubscriptionRecord
        {
            UserId = userId,
            PlanHandle = planHandle,
            SubscriptionReference = SubscriptionReference(userId, planHandle),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _identityDb.MaxioSubscriptionRecords.Add(record);
        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
            return record;
        }
        catch (DbUpdateException)
        {
            // A second process won the unique (user, plan) record. It uses the same deterministic reference.
            _identityDb.ChangeTracker.Clear();
            return await _identityDb.MaxioSubscriptionRecords.SingleAsync(existingRecord =>
                existingRecord.UserId == userId && existingRecord.PlanHandle == planHandle, cancellationToken);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existing = await FindCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var names = CustomerNames(user.Email!);
        try
        {
            using (MaxioSingleSendHandler.BeginWriteScope())
            {
                var response = await BoundedAsync(ct => _client.Customers.CreateCustomer(new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = names.First,
                        LastName = names.Last,
                        Email = user.Email!,
                        Reference = reference
                    }
                }, ct), cancellationToken);

                return response.Customer;
            }
        }
        catch (SdkException<CreateCustomerError> error)
        {
            var afterRace = await FindCustomerAsync(reference, cancellationToken);
            if (afterRace is not null)
            {
                return afterRace;
            }

            if (error.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new SubscriptionBillingException((int)HttpStatusCode.UnprocessableEntity,
                    "Maxio rejected the customer enrollment.", error);
            }

            throw ProviderFailure(error);
        }
        catch (MaxioWriteRetryBlockedException error)
        {
            var afterRetry = await FindCustomerAsync(reference, cancellationToken);
            if (afterRetry is not null)
            {
                return afterRetry;
            }

            throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway,
                "The customer enrollment outcome could not be confirmed. Please retry.", error);
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Customers.ReadCustomerByReference(reference, ct),
                cancellationToken, preserveRawProviderError: true);
            return response.Customer;
        }
        catch (SdkException<RawError> error) when (error.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Subscriptions.FindSubscription(reference, ct), cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> error) when (error.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (SdkException<FindSubscriptionError> error)
        {
            throw ProviderFailure(error);
        }
    }

    private async Task<Subscription> CreateSubscriptionAndReconcileAsync(
        Customer customer,
        SubscriptionPlanDto plan,
        string reference,
        CancellationToken cancellationToken)
    {
        if (customer.Id is null)
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway,
                "Maxio returned a customer without an identifier.");
        }

        try
        {
            using (MaxioSingleSendHandler.BeginWriteScope())
            {
                var response = await BoundedAsync(ct => _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customer.Id,
                        Reference = reference,
                        // The configured sandbox accepts cardless enrollments through Relationship Invoicing.
                        PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                    }
                }, ct), cancellationToken);

                return response.Subscription ?? throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway,
                    "Maxio returned an empty subscription response.");
            }
        }
        catch (SdkException<CreateSubscriptionError> error)
        {
            var afterFailure = await FindSubscriptionAsync(reference, cancellationToken);
            if (afterFailure is not null)
            {
                EnsureSubscriptionOwnership(afterFailure, customer, plan.Handle);
                return afterFailure;
            }

            if (error.Error.TryGetErrorListResponse1(out _))
            {
                error.Error.TryGetErrorListResponse1(out var validation);
                _logger.LogWarning("Maxio subscription validation failed: {Errors}",
                    string.Join(" | ", validation.Errors));
                throw new SubscriptionBillingException((int)HttpStatusCode.UnprocessableEntity,
                    "Maxio rejected the subscription enrollment.", error);
            }

            throw ProviderFailure(error);
        }
        catch (MaxioWriteRetryBlockedException error)
        {
            var afterRetry = await FindSubscriptionAsync(reference, cancellationToken);
            if (afterRetry is not null)
            {
                EnsureSubscriptionOwnership(afterRetry, customer, plan.Handle);
                return afterRetry;
            }

            throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway,
                "The subscription enrollment outcome could not be confirmed. Please retry.", error);
        }
    }

    private static void EnsureSubscriptionOwnership(Subscription subscription, Customer customer, string planHandle)
    {
        if (subscription.Customer?.Id != customer.Id || !string.Equals(subscription.Product?.Handle, planHandle,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.Conflict,
                "The existing subscription reference belongs to a different enrollment.");
        }
    }

    private static SubscriptionPlanDto MapPlan(Product product) => new(
        product.Handle!,
        product.Name ?? product.Handle!,
        product.Description,
        product.PriceInCents,
        product.Interval,
        product.IntervalUnit?.Value,
        product.RequireCreditCard);

    private static SubscriptionDto MapSubscription(Subscription subscription) => new(
        subscription.Id,
        subscription.Reference,
        subscription.Product?.Handle,
        subscription.Product?.Name,
        subscription.CurrentBillingAmountInCents ?? subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        subscription.State?.Value,
        subscription.NextAssessmentAt);

    private async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken,
        bool preserveRawProviderError = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ProviderCallBudget);
        try
        {
            return await action(deadline.Token);
        }
        catch (SdkException<RawError> error) when (!preserveRawProviderError)
        {
            throw ProviderFailure(error);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new SubscriptionBillingException((int)HttpStatusCode.BadGateway,
                "Maxio could not be reached or returned an unreadable response.", error);
        }
    }

    private static SubscriptionBillingException ProviderFailure(SdkException<RawError> error)
    {
        var statusCode = (int)error.Error.StatusCode;
        return new SubscriptionBillingException(statusCode is >= 400 and < 500 ? statusCode : (int)HttpStatusCode.BadGateway,
            "Maxio could not process the billing request.", error);
    }

    private static SubscriptionBillingException ProviderFailure<TError>(SdkException<TError> error)
    {
        return new SubscriptionBillingException((int)HttpStatusCode.BadGateway,
            "Maxio could not process the billing request.", error);
    }

    private static string CustomerReference(string userId) => $"eshop-user-{StableHash(userId)}";
    private static string SubscriptionReference(string userId, string planHandle) =>
        $"eshop-sub-{StableHash($"{userId}|{planHandle.ToLowerInvariant()}")}";

    private static string StableHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static (string First, string Last) CustomerNames(string email)
    {
        var localPart = email.Split('@')[0];
        var pieces = localPart.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var first = pieces.FirstOrDefault() ?? "Shopper";
        var last = pieces.Skip(1).FirstOrDefault() ?? "Customer";
        return (first, last);
    }
}
