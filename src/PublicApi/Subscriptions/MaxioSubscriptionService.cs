using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using MaxioAdvancedBilling.Servers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService
{
    private const int PerPage = 200;
    private const int MaxPlanPages = 10;
    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _catalog;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, CatalogContext catalog,
        UserManager<ApplicationUser> users, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _catalog = catalog;
        _users = users;
        _logger = logger;
    }

    public async Task<SubscriptionPlansResponse> GetPlansAsync(CancellationToken cancellationToken)
    {
        var result = new List<SubscriptionPlanDto>();
        var truncated = false;
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest { ProductFamilyId = $"handle:{GetFamilyHandle()}", Page = page, PerPage = PerPage }, cancellationToken: cancellationToken);
            var visible = products.Where(x => x.Product?.ArchivedAt is null).Select(x => x.Product!).ToList();
            result.AddRange(visible.Select(p => new SubscriptionPlanDto(p.Handle, p.Name, p.Description, p.PriceInCents, p.Interval, p.IntervalUnit)));
            if (products.Count < PerPage) break;
            if (page == MaxPlanPages) truncated = true;
        }
        return new SubscriptionPlansResponse(result, truncated);
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle)) throw new SubscriptionValidationException("productHandle is required.");
        var available = await GetPlansAsync(cancellationToken);
        if (!available.Plans.Any(p => string.Equals(p.Handle, productHandle, StringComparison.Ordinal)))
            throw new SubscriptionValidationException("The selected plan is not available in the configured product family.");
        if (available.IsTruncated) throw new SubscriptionProviderException("The plan catalog is incomplete; please try again later.", HttpStatusCode.ServiceUnavailable);

        var subscriptionReference = $"eshop:subscription:{userId}:{productHandle}";
        var claim = await GetOrClaimAsync($"subscription:{userId}:{productHandle}", userId, "subscription", subscriptionReference, cancellationToken);
        if (claim.Status == "Completed") return await FindSubscriptionAsync(subscriptionReference, cancellationToken);

        var customer = await EnsureCustomerAsync(userId, cancellationToken);
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customer.Id ?? throw new SubscriptionProviderException("Maxio returned a customer without an ID.", HttpStatusCode.BadGateway),
                        PaymentCollectionMethod = CollectionMethod.Invoice,
                        Reference = subscriptionReference
                    }
                }
            }, cancellationToken: cancellationToken);
            var subscription = response.Subscription ?? throw new SubscriptionProviderException("Maxio returned an incomplete subscription response.", HttpStatusCode.BadGateway);
            claim.MaxioResourceId = subscription.Id.ToString();
            claim.Status = "Completed";
            claim.CompletedAt = DateTimeOffset.UtcNow;
            await _catalog.SaveChangesAsync(cancellationToken);
            return ToDto(subscription);
        }
        catch (SdkConnectionException) { return await ReconcileSubscriptionAsync(claim, cancellationToken); }
        catch (ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var errors) ? string.Join("; ", errors.Errors) : "Maxio rejected the subscription.";
            _logger.LogWarning("Maxio rejected subscription for user {UserId}: {Detail}", userId, detail);
            throw new SubscriptionValidationException("Maxio rejected the subscription: " + detail);
        }
    }

    public async Task<MySubscriptionsResponse> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken)
    {
        Customer customer;
        try { customer = await ReadCustomerAsync(CustomerReference(userId), cancellationToken); }
        catch (MaxioNotFoundException) { return new MySubscriptionsResponse([]); }
        var subscriptions = await _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = customer.Id ?? throw new SubscriptionProviderException("Maxio returned a customer without an ID.", HttpStatusCode.BadGateway) }, cancellationToken: cancellationToken);
        return new MySubscriptionsResponse(subscriptions.Where(x => x.Subscription is not null).Select(x => ToDto(x.Subscription!)).ToList());
    }

    private async Task<Customer> EnsureCustomerAsync(string userId, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(userId);
        var claim = await GetOrClaimAsync($"customer:{reference}", userId, "customer", reference, cancellationToken);
        try
        {
            var existing = await ReadCustomerAsync(reference, cancellationToken);
            await CompleteClaimAsync(claim, existing.Id.ToString(), cancellationToken);
            return existing;
        }
        catch (MaxioNotFoundException) { }

        var user = await _users.FindByIdAsync(userId) ?? throw new SubscriptionValidationException("The authenticated user no longer exists.");
        if (string.IsNullOrWhiteSpace(user.Email)) throw new SubscriptionValidationException("An email address is required before subscribing.");
        var name = user.UserName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new SubscriptionValidationException("A user name is required before subscribing.");
        var split = name.Split([' ', '.', '_', '-'], 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = split.FirstOrDefault() ?? name;
        var lastName = split.Skip(1).FirstOrDefault() ?? "Customer";
        try
        {
            var response = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest { Customer = new CreateCustomer { Reference = reference, Email = user.Email, FirstName = firstName, LastName = lastName } }
            }, cancellationToken: cancellationToken);
            var customer = response.Customer ?? throw new SubscriptionProviderException("Maxio returned an incomplete customer response.", HttpStatusCode.BadGateway);
            await CompleteClaimAsync(claim, customer.Id.ToString(), cancellationToken);
            return customer;
        }
        catch (SdkConnectionException) { return await ReconcileCustomerAsync(claim, cancellationToken); }
    }

    private async Task<Customer> ReconcileCustomerAsync(MaxioOperationClaim claim, CancellationToken cancellationToken)
    {
        try
        {
            var customer = await ReadCustomerAsync(claim.Reference, cancellationToken);
            await CompleteClaimAsync(claim, customer.Id.ToString(), cancellationToken);
            return customer;
        }
        catch (MaxioNotFoundException)
        {
            claim.Status = "Unknown";
            await _catalog.SaveChangesAsync(cancellationToken);
            throw new SubscriptionProviderException("The customer creation outcome is unknown. Please retry later.", HttpStatusCode.ServiceUnavailable);
        }
    }

    private async Task<SubscriptionDto> ReconcileSubscriptionAsync(MaxioOperationClaim claim, CancellationToken cancellationToken)
    {
        try
        {
            var dto = await FindSubscriptionAsync(claim.Reference, cancellationToken);
            await CompleteClaimAsync(claim, dto.Id, cancellationToken);
            return dto;
        }
        catch (MaxioNotFoundException)
        {
            claim.Status = "Unknown";
            await _catalog.SaveChangesAsync(cancellationToken);
            throw new SubscriptionProviderException("The subscription creation outcome is unknown. Please retry later.", HttpStatusCode.ServiceUnavailable);
        }
    }

    private async Task<SubscriptionDto> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: cancellationToken);
            return response.Subscription is null ? throw new SubscriptionProviderException("Maxio returned an incomplete subscription response.", HttpStatusCode.BadGateway) : ToDto(response.Subscription);
        }
        catch (ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound) { throw new MaxioNotFoundException(); }
    }

    private async Task<Customer> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: cancellationToken);
            return response.Customer ?? throw new SubscriptionProviderException("Maxio returned an incomplete customer response.", HttpStatusCode.BadGateway);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound) { throw new MaxioNotFoundException(); }
    }

    private async Task<MaxioOperationClaim> GetOrClaimAsync(string id, string userId, string kind, string reference, CancellationToken cancellationToken)
    {
        var existing = await _catalog.MaxioOperationClaims.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is not null) return existing;
        var claim = new MaxioOperationClaim { Id = id, UserId = userId, Kind = kind, Reference = reference };
        _catalog.MaxioOperationClaims.Add(claim);
        try { await _catalog.SaveChangesAsync(cancellationToken); return claim; }
        catch (DbUpdateException)
        {
            _catalog.Entry(claim).State = EntityState.Detached;
            return await _catalog.MaxioOperationClaims.SingleAsync(x => x.Id == id, cancellationToken);
        }
    }

    private async Task CompleteClaimAsync(MaxioOperationClaim claim, string? resourceId, CancellationToken cancellationToken)
    {
        claim.MaxioResourceId = resourceId;
        claim.Status = "Completed";
        claim.CompletedAt = DateTimeOffset.UtcNow;
        await _catalog.SaveChangesAsync(cancellationToken);
    }

    private string GetFamilyHandle() => _settings.ProductFamilyHandle;
    private readonly MaxioSettings _settings = null!;
    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, CatalogContext catalog, UserManager<ApplicationUser> users, ILogger<MaxioSubscriptionService> logger, MaxioSettings settings) : this(client, catalog, users, logger) => _settings = settings;
    private static string CustomerReference(string userId) => $"eshop:{userId}";
    private static SubscriptionDto ToDto(Subscription x) => new(x.Id.ToString(), x.Reference, x.Product?.Handle, x.Product?.Name, x.ProductPriceInCents, x.State, x.NextAssessmentAt);
}

public sealed class SubscriptionValidationException(string message) : Exception(message);
public sealed class SubscriptionProviderException(string message, HttpStatusCode statusCode) : Exception(message) { public HttpStatusCode StatusCode { get; } = statusCode; }
public sealed class MaxioNotFoundException : Exception;
