using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>Closing a shop account erases the shopper's data at Stax and in the shop.</summary>
public class AccountService
{
    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly UserManager<ApplicationUser> _users;
    private readonly OrderLocks _locks;

    public AccountService(CatalogContext db, StaxGateway stax, UserManager<ApplicationUser> users, OrderLocks locks)
    {
        _db = db;
        _stax = stax;
        _users = users;
        _locks = locks;
    }

    public async Task CloseAsync(Shopper shopper, CancellationToken ct)
    {
        var buyerId = shopper.BuyerId;

        var orderIds = await _db.OrderPayments.Where(p => p.BuyerId == buyerId).Select(p => p.OrderId).ToListAsync(ct);
        var held = new List<IDisposable>();
        try
        {
            foreach (var id in orderIds.OrderBy(i => i)) held.Add(await _locks.LockAsync(id, ct));

            // Money still moving cannot be followed up once the shopper's records are gone.
            var open = await _db.OrderPayments.AnyAsync(p => p.BuyerId == buyerId &&
                (p.Status == PaymentStatuses.Pending || p.Status == PaymentStatuses.Processing || p.RefundStatus == RefundStatuses.Pending), ct);
            if (open)
                throw PaymentRequestException.Conflict(
                    "The account cannot be closed yet: a bank payment or a refund is still being processed. Try again once it has completed.");

            await EraseAtStaxAsync(shopper, ct);
            await EraseInShopAsync(shopper, ct);
        }
        finally
        {
            foreach (var h in held) h.Dispose();
        }
    }

    private async Task EraseAtStaxAsync(Shopper shopper, CancellationToken ct)
    {
        var buyerId = shopper.BuyerId;
        var link = await _db.PaymentCustomers.FirstOrDefaultAsync(c => c.BuyerId == buyerId, ct);
        var methodIds = (await _db.SavedPaymentMethods.Where(m => m.BuyerId == buyerId).Select(m => m.StaxPaymentMethodId).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (link is not null)
        {
            foreach (var pm in await _stax.ListCustomerPaymentMethodsAsync(link.StaxCustomerId, ct))
            {
                if (pm.Str("id") is { } id) methodIds.Add(id);
            }
        }

        foreach (var id in methodIds)
        {
            var pm = await _stax.GetPaymentMethodAsync(id, ct);
            if (pm is null) continue;
            var removed = pm.Value.Time("deleted_at") is not null;
            if (!removed)
            {
                await _stax.ScrubPaymentMethodAsync(id, ct);
                await _stax.DeletePaymentMethodAsync(id, ct);
            }
            else
            {
                await TryScrubPaymentMethodAsync(id, ct);
            }
        }

        var scheduleRecords = await _db.InvoiceSchedules
            .Where(s => s.CustomerEmail == shopper.Email || (link != null && s.StaxCustomerId == link.StaxCustomerId))
            .ToListAsync(ct);
        foreach (var schedule in scheduleRecords)
        {
            try
            {
                await _stax.DeleteInvoiceScheduleAsync(schedule.StaxScheduleId, ct);
            }
            catch (StaxApiException ex) when (ex.StaxStatus == 404)
            {
            }
        }
        _db.InvoiceSchedules.RemoveRange(scheduleRecords);

        if (link is not null)
        {
            // Stax keeps a deleted customer, so the personal details are blanked before it is deleted
            await _stax.UpdateCustomerAsync(link.StaxCustomerId, new Dictionary<string, object?>
            {
                ["firstname"] = "Closed",
                ["lastname"] = "Account",
                ["email"] = "",
                ["company"] = "",
                ["phone"] = "",
                ["address_1"] = "",
                ["address_2"] = "",
                ["address_city"] = "",
                ["address_state"] = "",
                ["address_zip"] = "",
                ["address_country"] = "",
                ["notes"] = "",
                ["reference"] = "",
                ["cc_emails"] = Array.Empty<string>(),
                ["cc_sms"] = Array.Empty<string>()
            }, ct);
            try
            {
                await _stax.DeleteCustomerAsync(link.StaxCustomerId, ct);
            }
            catch (StaxApiException ex) when (ex.StaxStatus is 404 or 422)
            {
                // already deleted
            }
        }
    }

    private async Task TryScrubPaymentMethodAsync(string id, CancellationToken ct)
    {
        try
        {
            await _stax.ScrubPaymentMethodAsync(id, ct);
        }
        catch (StaxApiException ex) when (ex.StaxStatus is >= 400 and < 500)
        {
            // Stax no longer lets a removed method be edited
        }
    }

    private async Task EraseInShopAsync(Shopper shopper, CancellationToken ct)
    {
        var buyerId = shopper.BuyerId;

        var orders = await _db.Orders.Include(o => o.OrderItems).Where(o => o.BuyerId == buyerId).ToListAsync(ct);
        _db.OrderItems.RemoveRange(orders.SelectMany(o => o.OrderItems));
        _db.Orders.RemoveRange(orders);
        _db.OrderPayments.RemoveRange(_db.OrderPayments.Where(p => p.BuyerId == buyerId));
        _db.SavedPaymentMethods.RemoveRange(_db.SavedPaymentMethods.Where(m => m.BuyerId == buyerId));
        _db.PaymentCustomers.RemoveRange(_db.PaymentCustomers.Where(c => c.BuyerId == buyerId));
        var baskets = await _db.Baskets.Include(b => b.Items).Where(b => b.BuyerId == buyerId).ToListAsync(ct);
        _db.BasketItems.RemoveRange(baskets.SelectMany(b => b.Items));
        _db.Baskets.RemoveRange(baskets);
        await _db.SaveChangesAsync(ct);

        var user = await _users.FindByNameAsync(buyerId);
        if (user is not null)
        {
            var result = await _users.DeleteAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("The account could not be removed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
