using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class AccountService
{
    private readonly StaxContext _db;
    private readonly CatalogContext _catalog;
    private readonly StaxClient _stax;
    private readonly ShopperService _shoppers;
    private readonly UserManager<ApplicationUser> _users;

    public AccountService(StaxContext db, CatalogContext catalog, StaxClient stax, ShopperService shoppers,
        UserManager<ApplicationUser> users)
    {
        _db = db;
        _catalog = catalog;
        _stax = stax;
        _shoppers = shoppers;
        _users = users;
    }

    /// <summary>
    /// Closes the shopper's account and erases their data at Stax (every payment method, removed ones
    /// included, and the customer record) and in the shop. Money that is still moving blocks the closure.
    /// </summary>
    public async Task CloseAsync(string email, CancellationToken ct)
    {
        var inFlight = await _db.ShopOrders.AnyAsync(o => o.ShopperEmail == email &&
            (o.Status == OrderStatuses.PendingSettlement || o.Status == OrderStatuses.Processing ||
             o.RefundStatus == RefundStatuses.Pending), ct);
        if (inFlight)
        {
            throw ApiException.Conflict(
                "The account cannot be closed while a payment is waiting to settle or a refund is in progress.");
        }

        // 1. Stax first: if it fails nothing local is lost and closing can simply be retried.
        foreach (var customerId in await _shoppers.ResolveCustomerIdsAsync(email, ct))
        {
            foreach (var pm in await _stax.ListPaymentMethodsAsync(customerId, ct))
            {
                var id = pm["id"]!.GetValue<string>();
                try
                {
                    await _stax.UpdatePaymentMethodAsync(id, new
                    {
                        person_name = "Closed account",
                        address_1 = "", address_2 = "", address_city = "", address_state = "",
                        address_zip = "", address_country = "",
                    }, ct);
                }
                catch (ApiException ex) when (ex.StatusCode is 404 or 422) { }

                if (string.IsNullOrEmpty(pm["deleted_at"]?.ToString()))
                {
                    try { await _stax.DeletePaymentMethodAsync(id, ct); }
                    catch (ApiException ex) when (ex.StatusCode is 404 or 422) { }
                }
            }

            try
            {
                await _stax.UpdateCustomerAsync(customerId, new
                {
                    firstname = "Closed account", lastname = "", company = "", email = "", phone = "",
                    address_1 = "", address_2 = "", address_city = "", address_state = "", address_zip = "",
                    address_country = "", notes = "", reference = "closed",
                }, ct);
                await _stax.DeleteCustomerAsync(customerId, ct);
            }
            catch (ApiException ex) when (ex.StatusCode is 404 or 422) { }
        }

        // 2. The shop's own data.
        var hash = ShopperService.HashEmail(email);
        _db.SavedPaymentMethods.RemoveRange(_db.SavedPaymentMethods.Where(p => p.ShopperEmail == email));
        _db.ShopperAccounts.RemoveRange(_db.ShopperAccounts.Where(a => a.Email == email));
        foreach (var order in await _db.ShopOrders.Where(o => o.ShopperEmail == email).ToListAsync(ct))
        {
            // Financial records stay, but no longer point at a person.
            order.ShopperEmail = "closed-" + hash[..16];
            order.PaymentMethodId = null;
            order.LastPaymentError = null;
        }
        if (!await _db.ClosedShoppers.AnyAsync(c => c.EmailHash == hash, ct))
        {
            _db.ClosedShoppers.Add(new ClosedShopper { EmailHash = hash, ClosedAt = DateTimeOffset.UtcNow });
        }
        await _db.SaveChangesAsync(ct);

        var buyerIds = new[] { email };
        var baskets = await _catalog.Baskets.Include(b => b.Items)
            .Where(b => buyerIds.Contains(b.BuyerId.ToLower())).ToListAsync(ct);
        _catalog.Baskets.RemoveRange(baskets);
        var shopOrders = await _catalog.Orders.Include(o => o.OrderItems)
            .Where(o => buyerIds.Contains(o.BuyerId.ToLower())).ToListAsync(ct);
        _catalog.Orders.RemoveRange(shopOrders);
        await _catalog.SaveChangesAsync(ct);

        var user = await _users.FindByNameAsync(email) ?? await _users.FindByEmailAsync(email);
        if (user != null) await _users.DeleteAsync(user);
    }
}
