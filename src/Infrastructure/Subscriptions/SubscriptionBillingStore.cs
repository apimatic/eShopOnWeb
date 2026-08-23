using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public sealed class SubscriptionBillingStore : ISubscriptionBillingStore
{
    private readonly CatalogContext _context;

    public SubscriptionBillingStore(CatalogContext context) => _context = context;

    public Task<MaxioCustomerMapping?> FindCustomerAsync(string applicationUserId, CancellationToken cancellationToken) =>
        _context.MaxioCustomers.SingleOrDefaultAsync(x => x.ApplicationUserId == applicationUserId, cancellationToken);

    public async Task<MaxioCustomerMapping> GetOrCreateCustomerAsync(
        string applicationUserId,
        string maxioReference,
        CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(applicationUserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var mapping = new MaxioCustomerMapping(applicationUserId, maxioReference);
        _context.MaxioCustomers.Add(mapping);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return mapping;
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            var winner = await FindCustomerAsync(applicationUserId, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return winner;
        }
    }

    public Task SaveCustomerAsync(MaxioCustomerMapping mapping, CancellationToken cancellationToken) =>
        SaveAsync(mapping, cancellationToken);

    public Task<RecurringSubscription?> FindSubscriptionAsync(
        string applicationUserId,
        string productHandle,
        CancellationToken cancellationToken) =>
        _context.RecurringSubscriptions.SingleOrDefaultAsync(
            x => x.ApplicationUserId == applicationUserId && x.ProductHandle == productHandle,
            cancellationToken);

    public async Task<SubscriptionReservation> GetOrCreateSubscriptionAsync(
        RecurringSubscription subscription,
        CancellationToken cancellationToken)
    {
        var existing = await FindSubscriptionAsync(
            subscription.ApplicationUserId,
            subscription.ProductHandle,
            cancellationToken);
        if (existing is not null)
        {
            return new SubscriptionReservation(existing, false);
        }

        _context.RecurringSubscriptions.Add(subscription);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new SubscriptionReservation(subscription, true);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            existing = await FindSubscriptionAsync(
                subscription.ApplicationUserId,
                subscription.ProductHandle,
                cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return new SubscriptionReservation(existing, false);
        }
    }

    public async Task<IReadOnlyList<RecurringSubscription>> ListSubscriptionsAsync(
        string applicationUserId,
        CancellationToken cancellationToken) =>
        await _context.RecurringSubscriptions
            .Where(x => x.ApplicationUserId == applicationUserId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task SaveSubscriptionAsync(RecurringSubscription subscription, CancellationToken cancellationToken) =>
        SaveAsync(subscription, cancellationToken);

    private async Task SaveAsync<TEntity>(TEntity entity, CancellationToken cancellationToken) where TEntity : class
    {
        if (_context.Entry(entity).State == EntityState.Detached)
        {
            _context.Update(entity);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
