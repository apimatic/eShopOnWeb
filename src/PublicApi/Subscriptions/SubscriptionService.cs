using System.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService : ISubscriptionService
{
    private const string CustomerReferencePrefix = "eshop:";
    private const string SubscriptionReferencePrefix = "eshop-subscription:";
    private readonly MaxioAdvancedBillingClient _maxio;
    private readonly AppIdentityDbContext _identity;
    private readonly UserManager<ApplicationUser> _users;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioAdvancedBillingClient maxio, AppIdentityDbContext identity,
        UserManager<ApplicationUser> users, Microsoft.Extensions.Options.IOptions<MaxioSettings> settings,
        ILogger<SubscriptionService> logger)
    {
        _maxio = maxio; _identity = identity; _users = users; _settings = settings.Value; _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<SubscriptionPlanDto>();
        for (var page = 1; ; page++)
        {
            var response = await _maxio.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest { ProductFamilyId = $"handle:{_settings.ProductFamilyHandle}", Page = page, PerPage = 200 },
                cancellationToken: cancellationToken);
            if (response.Count == 0) break;
            plans.AddRange(response.Select(x => ToPlan(x.Product)).Where(x => x is not null)!);
            if (response.Count < 200) break;
        }
        return plans;
    }

    public async Task<SubscriptionResult> SubscribeAsync(string userId, string userName, string email, string productHandle, CancellationToken cancellationToken)
    {
        var plan = (await GetPlansAsync(cancellationToken)).SingleOrDefault(x => string.Equals(x.Handle, productHandle, StringComparison.Ordinal));
        if (plan is null) throw new InvalidOperationException("The requested subscription plan is unavailable.");

        var existing = await _identity.SubscriptionEnrollments.FindAsync([userId, productHandle], cancellationToken);
        if (existing?.MaxioSubscriptionId is int existingId)
            return new SubscriptionResult(await ReadSubscriptionAsync(existingId, plan.PriceInCents, cancellationToken), true);

        var reference = $"{SubscriptionReferencePrefix}{userId}:{productHandle}";
        var claim = new SubscriptionEnrollment { UserId = userId, ProductHandle = productHandle, RequestReference = reference };
        _identity.SubscriptionEnrollments.Add(claim);
        try { await _identity.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            var concurrent = await _identity.SubscriptionEnrollments.FindAsync([userId, productHandle], cancellationToken);
            if (concurrent?.MaxioSubscriptionId is int concurrentId)
                return new SubscriptionResult(await ReadSubscriptionAsync(concurrentId, plan.PriceInCents, cancellationToken), true);
            throw new InvalidOperationException("A subscription request for this plan is already being processed.");
        }

        var customerId = await EnsureCustomerAsync(userId, userName, email, cancellationToken);
        Subscription? subscription;
        try
        {
            subscription = (await _maxio.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest { Body = new CreateSubscriptionRequest { Subscription = new CreateSubscription { CustomerId = customerId, ProductHandle = productHandle, Reference = reference, PaymentCollectionMethod = CollectionMethod.Invoice } } },
                cancellationToken: cancellationToken)).Subscription;
        }
        catch (SdkConnectionException)
        {
            subscription = await FindSubscriptionAsync(reference, cancellationToken);
            if (subscription is null) throw;
        }
        catch (ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            _identity.SubscriptionEnrollments.Remove(claim);
            await _identity.SaveChangesAsync(cancellationToken);
            if (ex.Error.TryGetErrorListResponse1(out var errors))
                _logger.LogWarning("Maxio rejected subscription request {Reference}: {Errors}", reference, string.Join("; ", errors.Errors));
            throw;
        }
        catch (ResponseDeserializationException)
        {
            _identity.SubscriptionEnrollments.Remove(claim);
            await _identity.SaveChangesAsync(cancellationToken);
            throw;
        }

        if (subscription?.Id is not int subscriptionId)
            throw new InvalidOperationException("Maxio did not return a subscription identifier.");
        claim.MaxioSubscriptionId = subscriptionId;
        await _identity.SaveChangesAsync(cancellationToken);
        return new SubscriptionResult(ToSubscription(subscription, plan.PriceInCents), false);
    }

    public async Task<IReadOnlyList<MySubscriptionDto>> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(CustomerReferencePrefix + userId, cancellationToken);
        if (customer?.Id is not int customerId) return [];
        var subscriptions = await _maxio.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: cancellationToken);
        return subscriptions.Where(x => x.Subscription is not null).Select(x => ToSubscription(x.Subscription!, null)).ToList();
    }

    private async Task<int> EnsureCustomerAsync(string userId, string userName, string email, CancellationToken ct)
    {
        var reference = CustomerReferencePrefix + userId;
        var existing = await FindCustomerAsync(reference, ct);
        if (existing?.Id is int id) return id;
        var names = userName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        try
        {
            var result = await _maxio.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest { Customer = new CreateCustomer { FirstName = names.FirstOrDefault() ?? email.Split('@')[0], LastName = names.Skip(1).FirstOrDefault() ?? "Customer", Email = email, Reference = reference } }
            }, cancellationToken: ct);
            return result.Customer.Id ?? throw new InvalidOperationException("Maxio did not return a customer identifier.");
        }
        catch (ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>)
        {
            var raced = await FindCustomerAsync(reference, ct);
            if (raced?.Id is int racedId) return racedId;
            throw;
        }
        catch (SdkConnectionException)
        {
            var raced = await FindCustomerAsync(reference, ct);
            if (raced?.Id is int racedId) return racedId;
            throw;
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try { return (await _maxio.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct)).Customer; }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try { return (await _maxio.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct)).Subscription; }
        catch (ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    private async Task<MySubscriptionDto> ReadSubscriptionAsync(int id, long? price, CancellationToken ct) =>
        ToSubscription((await _maxio.Subscriptions.ReadSubscription(new MaxioAdvancedBilling.Requests.Subscriptions.ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct)).Subscription ?? throw new InvalidOperationException("Maxio did not return a subscription."), price);
    private static SubscriptionPlanDto? ToPlan(Product? product) => product?.Handle is null ? null : new(product.Handle, product.Name ?? product.Handle, product.Description, product.PriceInCents, product.Interval, product.IntervalUnit?.Value);
    private static MySubscriptionDto ToSubscription(Subscription subscription, long? price) => new(subscription.Id, subscription.Product?.Handle, subscription.State?.Value, subscription.CurrentPeriodEndsAt, price ?? subscription.ProductPriceInCents);
}
