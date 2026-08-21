using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class EfSubscriptionLinkStore : ISubscriptionLinkStore
{
    private readonly CatalogContext _context;

    public EfSubscriptionLinkStore(CatalogContext context)
    {
        _context = context;
    }

    public Task<MaxioCustomerLink?> FindCustomerAsync(
        string userId,
        CancellationToken cancellationToken) =>
        _context.MaxioCustomerLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(link => link.UserId == userId, cancellationToken);

    public async Task SaveCustomerAsync(
        MaxioCustomerLink customer,
        CancellationToken cancellationToken)
    {
        var existing = await _context.MaxioCustomerLinks
            .SingleOrDefaultAsync(link => link.UserId == customer.UserId, cancellationToken);
        if (existing is not null)
        {
            existing.Refresh(customer.MaxioCustomerId, customer.UpdatedAt);
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        _context.MaxioCustomerLinks.Add(customer);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.Entry(customer).State = EntityState.Detached;
            existing = await _context.MaxioCustomerLinks
                .SingleOrDefaultAsync(link => link.UserId == customer.UserId, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            existing.Refresh(customer.MaxioCustomerId, customer.UpdatedAt);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public Task<MaxioSubscriptionLink?> FindSubscriptionAsync(
        string userId,
        string productHandle,
        string pricePointHandle,
        CancellationToken cancellationToken) =>
        _context.MaxioSubscriptionLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(link =>
                link.UserId == userId &&
                link.ProductHandle == productHandle &&
                link.PricePointHandle == pricePointHandle,
                cancellationToken);

    public async Task<SubscriptionClaim> ClaimSubscriptionAsync(
        string userId,
        string productHandle,
        string pricePointHandle,
        string subscriptionReference,
        Guid leaseId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _context.MaxioSubscriptionLinks.SingleOrDefaultAsync(link =>
            link.UserId == userId &&
            link.ProductHandle == productHandle &&
            link.PricePointHandle == pricePointHandle,
            cancellationToken);

        if (existing is null)
        {
            var created = new MaxioSubscriptionLink(
                userId,
                productHandle,
                pricePointHandle,
                subscriptionReference,
                leaseId,
                now);
            _context.MaxioSubscriptionLinks.Add(created);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return new SubscriptionClaim(created, Acquired: true);
            }
            catch (DbUpdateException)
            {
                _context.Entry(created).State = EntityState.Detached;
                existing = await _context.MaxioSubscriptionLinks.SingleOrDefaultAsync(link =>
                    link.UserId == userId &&
                    link.ProductHandle == productHandle &&
                    link.PricePointHandle == pricePointHandle,
                    cancellationToken);
                if (existing is null)
                {
                    throw;
                }
            }
        }

        if (!existing.TryAcquire(leaseId, now))
        {
            return new SubscriptionClaim(existing, Acquired: false);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new SubscriptionClaim(existing, Acquired: true);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.Entry(existing).State = EntityState.Detached;
            var winner = await _context.MaxioSubscriptionLinks
                .AsNoTracking()
                .SingleAsync(link =>
                    link.UserId == userId &&
                    link.ProductHandle == productHandle &&
                    link.PricePointHandle == pricePointHandle,
                    cancellationToken);
            return new SubscriptionClaim(winner, Acquired: false);
        }
    }

    public async Task ConfirmSubscriptionAsync(
        MaxioSubscriptionLink link,
        Guid leaseId,
        int maxioSubscriptionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        link.Confirm(leaseId, maxioSubscriptionId, now);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task FailSubscriptionAsync(
        MaxioSubscriptionLink link,
        Guid leaseId,
        string safeErrorCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        link.Fail(leaseId, safeErrorCode, now);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
