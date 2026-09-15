using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserPlanLocks = new();
    private readonly CatalogContext _catalogContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _options;

    public SubscriptionService(CatalogContext catalogContext,
        UserManager<ApplicationUser> userManager,
        IMaxioClient maxioClient,
        IOptions<MaxioOptions> options)
    {
        _catalogContext = catalogContext;
        _userManager = userManager;
        _maxioClient = maxioClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        var products = await _maxioClient.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(IsProductInConfiguredFamily)
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null)
            .OrderBy(product => product.Name)
            .Select(ToPlanDto)
            .ToList();
    }

    public async Task<SubscriptionDto?> SubscribeAsync(ApplicationUser user, string planHandle,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        if (string.IsNullOrWhiteSpace(planHandle))
            return null;

        var product = await _maxioClient.GetProductByHandleAsync(planHandle.Trim(), cancellationToken);
        if (product is null || product.ArchivedAt is not null || !IsProductInConfiguredFamily(product))
            return null;

        var key = $"{user.Id}:{product.Handle}";
        var gate = UserPlanLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(user, cancellationToken);
            var reference = SubscriptionReference(user.Id, product.Handle!);
            var mapping = await _catalogContext.SubscriptionMappings
                .SingleOrDefaultAsync(item => item.UserId == user.Id && item.PlanHandle == product.Handle, cancellationToken);

            if (mapping?.MaxioSubscriptionId is int existingId)
            {
                var existing = await _maxioClient.GetSubscriptionAsync(existingId, cancellationToken);
                if (existing is not null)
                {
                    await UpdateMappingAsync(mapping, customer.Id, existing, cancellationToken);
                    return ToSubscriptionDto(existing, product);
                }

                mapping.MaxioSubscriptionId = null;
                mapping.State = SubscriptionMappingStates.Pending;
                mapping.UpdatedAtUtc = DateTime.UtcNow;
                await _catalogContext.SaveChangesAsync(cancellationToken);
            }

            // A remote reference lookup recovers a successful Maxio write even if the local
            // process stopped before it persisted the returned subscription id.
            var remote = await _maxioClient.FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (remote is not null)
            {
                mapping ??= await ReserveMappingAsync(user.Id, product.Handle!, customer.Id, cancellationToken);
                await UpdateMappingAsync(mapping, customer.Id, remote, cancellationToken);
                return ToSubscriptionDto(remote, product);
            }

            if (mapping is not null && mapping.State == SubscriptionMappingStates.Pending &&
                DateTime.UtcNow - mapping.UpdatedAtUtc < TimeSpan.FromMinutes(5))
            {
                throw new SubscriptionOperationInProgressException();
            }

            mapping ??= await ReserveMappingAsync(user.Id, product.Handle!, customer.Id, cancellationToken);
            mapping.MaxioCustomerId = customer.Id;
            mapping.State = SubscriptionMappingStates.Pending;
            mapping.UpdatedAtUtc = DateTime.UtcNow;
            await _catalogContext.SaveChangesAsync(cancellationToken);

            MaxioSubscription subscription;
            try
            {
                var site = await _maxioClient.GetSiteAsync(cancellationToken);
                subscription = await _maxioClient.CreateSubscriptionAsync(new MaxioSubscriptionAttributes
                {
                    ProductHandle = product.Handle!,
                    CustomerId = customer.Id,
                    Reference = reference,
                    PaymentCollectionMethod = site.RelationshipInvoicingEnabled ? "remittance" : "invoice"
                }, cancellationToken);
            }
            catch (MaxioApiException exception) when (exception.StatusCode == 422)
            {
                // A concurrent request may have won the remote create. The documented
                // reference lookup is the safe reconciliation path.
                subscription = await _maxioClient.FindSubscriptionByReferenceAsync(reference, cancellationToken)
                    ?? await MarkFailedAndRethrowAsync(mapping, exception, cancellationToken);
            }
            catch
            {
                mapping.State = SubscriptionMappingStates.Failed;
                mapping.UpdatedAtUtc = DateTime.UtcNow;
                await _catalogContext.SaveChangesAsync(cancellationToken);
                throw;
            }

            await UpdateMappingAsync(mapping, customer.Id, subscription, cancellationToken);
            return ToSubscriptionDto(subscription, product);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ApplicationUser user,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration();
        var customer = await _maxioClient.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var result = new List<SubscriptionDto>();
        foreach (var subscription in subscriptions.Where(item => IsProductInConfiguredFamily(item.Product)))
        {
            var product = subscription.Product!;
            result.Add(ToSubscriptionDto(subscription, product));

            var planHandle = product.Handle;
            if (string.IsNullOrWhiteSpace(planHandle))
                continue;

            var mapping = await _catalogContext.SubscriptionMappings
                .SingleOrDefaultAsync(item => item.UserId == user.Id && item.PlanHandle == planHandle, cancellationToken);
            if (mapping is null)
            {
                mapping = new SubscriptionMapping
                {
                    UserId = user.Id,
                    PlanHandle = planHandle,
                    MaxioCustomerId = customer.Id,
                    MaxioSubscriptionId = subscription.Id,
                    State = subscription.State,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                _catalogContext.SubscriptionMappings.Add(mapping);
            }
            else
            {
                mapping.MaxioCustomerId = customer.Id;
                mapping.MaxioSubscriptionId = subscription.Id;
                mapping.State = subscription.State;
                mapping.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await _catalogContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existing = await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
            return existing;

        var email = user.Email ?? user.UserName ?? $"{user.Id}@invalid.local";
        var lastName = user.UserName ?? user.Email ?? user.Id;
        try
        {
            return await _maxioClient.CreateCustomerAsync(new MaxioCustomerAttributes
            {
                FirstName = "eShopOnWeb",
                LastName = lastName,
                Email = email,
                Reference = reference
            }, cancellationToken);
        }
        catch (MaxioApiException exception) when (exception.StatusCode == 422)
        {
            // Customer reference is unique according to the Maxio API. If another request
            // created it first, reconcile by reading that exact reference.
            return await _maxioClient.FindCustomerByReferenceAsync(reference, cancellationToken)
                ?? throw new MaxioApiException(exception.StatusCode, exception.ResponseBody);
        }
    }

    private async Task<SubscriptionMapping> ReserveMappingAsync(string userId, string planHandle,
        int customerId, CancellationToken cancellationToken)
    {
        var mapping = new SubscriptionMapping
        {
            UserId = userId,
            PlanHandle = planHandle,
            MaxioCustomerId = customerId,
            State = SubscriptionMappingStates.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _catalogContext.SubscriptionMappings.Add(mapping);
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return mapping;
        }
        catch (DbUpdateException)
        {
            _catalogContext.Entry(mapping).State = EntityState.Detached;
            return await _catalogContext.SubscriptionMappings.SingleAsync(item =>
                item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
        }
    }

    private async Task UpdateMappingAsync(SubscriptionMapping mapping, int customerId,
        MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        mapping.MaxioCustomerId = customerId;
        mapping.MaxioSubscriptionId = subscription.Id;
        mapping.State = subscription.State;
        mapping.UpdatedAtUtc = DateTime.UtcNow;
        await _catalogContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<MaxioSubscription> MarkFailedAndRethrowAsync(SubscriptionMapping mapping,
        MaxioApiException exception, CancellationToken cancellationToken)
    {
        mapping.State = SubscriptionMappingStates.Failed;
        mapping.UpdatedAtUtc = DateTime.UtcNow;
        await _catalogContext.SaveChangesAsync(cancellationToken);
        throw exception;
    }

    private bool IsProductInConfiguredFamily(MaxioProduct? product) =>
        product?.ProductFamily is not null &&
        string.Equals(product.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal);

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard,
        Taxable = product.Taxable
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, MaxioProduct product) => new()
    {
        Id = subscription.Id,
        PlanHandle = product.Handle ?? string.Empty,
        PlanName = product.Name,
        PriceInCents = subscription.ProductPriceInCents != 0 ? subscription.ProductPriceInCents : product.PriceInCents,
        State = subscription.State,
        NextBillingDate = subscription.CurrentPeriodEndsAt
    };

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio configuration is incomplete. Configure Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle.");
        }
    }

    private static string CustomerReference(string userId) => $"eshoponweb-customer-{Hash(userId)}";

    private static string SubscriptionReference(string userId, string planHandle) =>
        $"eshoponweb-subscription-{Hash($"{userId}|{planHandle}")}";

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
