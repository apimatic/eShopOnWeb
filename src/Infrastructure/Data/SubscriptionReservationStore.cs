using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public sealed class SubscriptionReservationStore : ISubscriptionReservationStore
{
    private readonly CatalogContext _context;

    public SubscriptionReservationStore(CatalogContext context)
    {
        _context = context;
    }

    public async Task<(SubscriptionReservation Reservation, bool Created)> GetOrCreateAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(userId, productHandle, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        var reservation = new SubscriptionReservation(
            userId,
            productHandle,
            customerReference,
            subscriptionReference);
        _context.SubscriptionReservations.Add(reservation);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return (reservation, true);
        }
        catch (DbUpdateException)
        {
            _context.Entry(reservation).State = EntityState.Detached;
            var winner = await FindAsync(userId, productHandle, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return (winner, false);
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        _context.SaveChangesAsync(cancellationToken);

    private Task<SubscriptionReservation?> FindAsync(
        string userId,
        string productHandle,
        CancellationToken cancellationToken) =>
        _context.SubscriptionReservations.SingleOrDefaultAsync(
            reservation => reservation.UserId == userId && reservation.ProductHandle == productHandle,
            cancellationToken);
}
