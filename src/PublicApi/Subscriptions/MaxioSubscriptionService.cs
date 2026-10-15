using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService
{
    private static readonly string[] OfferedHandles = ["eshop-pro", "basic-plan"];
    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, CatalogContext db,
        UserManager<ApplicationUser> users, IOptions<MaxioSettings> settings, ILogger<MaxioSubscriptionService> logger)
    { _client = client; _db = db; _users = users; _settings = settings.Value; _logger = logger; }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlanDto>();
        foreach (var handle in OfferedHandles)
        {
            try
            {
                var response = await _client.Products.ReadProductByHandle(new ReadProductByHandleRequest { ApiHandle = handle }, cancellationToken: ct);
                var product = response.Product;
                if (product?.ProductFamily?.Handle == _settings.ProductFamilyHandle)
                    plans.Add(new SubscriptionPlanDto(product.Handle ?? string.Empty, product.Name ?? string.Empty, (product.PriceInCents ?? 0) / 100m));
            }
            catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
            catch (SdkException ex) { throw new MaxioUnavailableException(ex.StatusCodeOrNull(), "Maxio could not load subscription plans.", ex); }
        }
        return plans;
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string planHandle, CancellationToken ct)
    {
        var plan = (await GetPlansAsync(ct)).SingleOrDefault(x => x.Handle == planHandle)
            ?? throw new MaxioRequestException(HttpStatusCode.BadRequest, "The requested subscription plan is unavailable.");
        var customerId = await ResolveCustomerAsync(userName, ct);
        var key = $"{userName}:{planHandle}";
        var reference = $"subscription:{userName}:{planHandle}";
        var enrollment = new MaxioSubscriptionEnrollment { Key = key, UserName = userName, PlanHandle = planHandle, Reference = reference };
        try { _db.MaxioSubscriptionEnrollments.Add(enrollment); await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            var prior = await _db.MaxioSubscriptionEnrollments.AsNoTracking().SingleAsync(x => x.Key == key, ct);
            if (prior.SubscriptionId is not null) return await FindSubscriptionAsync(reference, ct);
            return await FindSubscriptionAsync(reference, ct);
        }
        try
        {
            var result = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            { Body = new CreateSubscriptionRequest { Subscription = new CreateSubscription { ProductHandle = plan.Handle, CustomerId = customerId, Reference = reference } } }, cancellationToken: ct);
            var subscription = result.Subscription ?? throw new MaxioUnavailableException(null, "Maxio returned an incomplete subscription response.");
            enrollment.SubscriptionId = subscription.Id; enrollment.Status = "Complete"; await _db.SaveChangesAsync(ct);
            return ToDto(subscription);
        }
        catch (SdkConnectionException)
        {
            var found = await FindSubscriptionAsync(reference, ct);
            enrollment.SubscriptionId = found.Id; enrollment.Status = "Complete"; await _db.SaveChangesAsync(ct); return found;
        }
        catch (SdkException ex) { enrollment.Status = "Failed"; await _db.SaveChangesAsync(ct); throw new MaxioUnavailableException(ex.StatusCodeOrNull(), "Maxio could not create the subscription.", ex); }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userName, CancellationToken ct)
    {
        var customer = await _db.MaxioCustomerResolutions.AsNoTracking().SingleOrDefaultAsync(x => x.UserName == userName, ct);
        if (customer?.CustomerId is null) return [];
        try
        {
            var values = await _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customer.CustomerId.Value }, cancellationToken: ct);
            return values.Select(x => x.Subscription).Where(x => x is not null).Select(x => ToDto(x!)).ToList();
        }
        catch (SdkException ex) { throw new MaxioUnavailableException(ex.StatusCodeOrNull(), "Maxio could not load subscriptions.", ex); }
    }

    private async Task<int> ResolveCustomerAsync(string userName, CancellationToken ct)
    {
        var claim = new MaxioCustomerResolution { UserName = userName };
        try { _db.MaxioCustomerResolutions.Add(claim); await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            var prior = await _db.MaxioCustomerResolutions.AsNoTracking().SingleAsync(x => x.UserName == userName, ct);
            if (prior.CustomerId is not null) return prior.CustomerId.Value;
            var found = await ReadCustomerAsync(userName, ct); if (found?.Id is not null) return found.Id.Value;
            throw new MaxioUnavailableException(null, "A subscription request is already being processed.");
        }
        var existing = await ReadCustomerAsync(userName, ct);
        if (existing?.Id is not null) { claim.CustomerId = existing.Id.Value; claim.Status = "Complete"; await _db.SaveChangesAsync(ct); return existing.Id.Value; }
        var user = await _users.FindByNameAsync(userName) ?? throw new MaxioRequestException(HttpStatusCode.Unauthorized, "Authenticated user no longer exists.");
        var email = user.Email ?? userName;
        var response = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest { Body = new CreateCustomerRequest { Customer = new CreateCustomer { FirstName = userName, LastName = "Shopper", Email = email, Reference = userName } } }, cancellationToken: ct);
        var created = response.Customer ?? throw new MaxioUnavailableException(null, "Maxio returned an incomplete customer response.");
        if (created.Id is null) throw new MaxioUnavailableException(null, "Maxio returned a customer without an ID.");
        claim.CustomerId = created.Id.Value; claim.Status = "Complete"; await _db.SaveChangesAsync(ct); return created.Id.Value;
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken ct)
    {
        try { return (await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct)).Customer; }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    private async Task<SubscriptionDto> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        var response = await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
        return ToDto(response.Subscription ?? throw new MaxioUnavailableException(null, "Subscription outcome remains pending."));
    }

    private static SubscriptionDto ToDto(Subscription s) => new(s.Id ?? 0, s.Product?.Handle ?? string.Empty, s.Product?.Name ?? string.Empty,
        (s.ProductPriceInCents ?? 0) / 100m, s.State?.Value ?? "unknown", s.NextAssessmentAt ?? s.CurrentPeriodEndsAt);
}

public sealed class MaxioRequestException(HttpStatusCode status, string message) : Exception(message) { public HttpStatusCode Status { get; } = status; }
public sealed class MaxioUnavailableException(HttpStatusCode? status, string message, Exception? inner = null) : Exception(message, inner) { public HttpStatusCode? Status { get; } = status; }
internal static class MaxioExceptionExtensions { public static HttpStatusCode? StatusCodeOrNull(this SdkException ex) => ex is ApiException api ? api.StatusCode : null; }
