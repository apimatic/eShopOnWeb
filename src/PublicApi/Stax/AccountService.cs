using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Stax;

public class AccountService
{
    private const string Erased = "Erased";

    private readonly CatalogContext _db;
    private readonly StaxClient _stax;
    private readonly ShopperService _shoppers;
    private readonly ShopOrderService _orders;
    private readonly UserManager<ApplicationUser> _users;

    public AccountService(CatalogContext db, StaxClient stax, ShopperService shoppers, ShopOrderService orders,
        UserManager<ApplicationUser> users)
    {
        _db = db;
        _stax = stax;
        _shoppers = shoppers;
        _orders = orders;
        _users = users;
    }

    /// <summary>Closes the account and erases the shopper's data at Stax and in the shop.</summary>
    public async Task CloseAsync(string email, CancellationToken ct)
    {
        var myOrders = await _db.Orders.Include(o => o.OrderItems)
            .Where(o => o.BuyerId == email).ToListAsync(ct);

        foreach (var order in myOrders)
        {
            await _orders.SyncAsync(order, ct);
        }
        if (myOrders.Any(o => o.PaymentState == PaymentState.Pending || o.RefundStatus == RefundStatus.Pending))
        {
            throw ApiException.Conflict(
                "The account can't be closed while a bank payment or a refund is still being processed. Try again once it has completed.");
        }

        // Stax first: if it fails nothing local has changed and the shopper can simply retry.
        try
        {
            foreach (var customerId in await _shoppers.FindCustomerIdsAsync(email, ct))
            {
                await EraseCustomerAsync(customerId, ct);
            }
        }
        catch (StaxException ex) when (ex.StaxStatusCode is >= 400 and < 500)
        {
            throw new ApiException(502, $"Stax could not erase the account data: {ex.Message}");
        }

        var normalized = email.Trim().ToLowerInvariant();
        _db.Orders.RemoveRange(myOrders);
        _db.ShopperStaxCustomers.RemoveRange(_db.ShopperStaxCustomers.Where(s => s.Email == normalized));
        _db.Baskets.RemoveRange(_db.Baskets.Include(b => b.Items).Where(b => b.BuyerId == email));
        await _db.SaveChangesAsync(ct);

        var user = await _users.FindByNameAsync(email);
        if (user is not null)
        {
            var result = await _users.DeleteAsync(user);
            if (!result.Succeeded)
            {
                throw new ApiException(500, "The account data was erased but the login could not be removed.");
            }
        }
    }

    private async Task EraseCustomerAsync(string customerId, CancellationToken ct)
    {
        var methods = await _stax.GetAsync($"customer/{customerId}/payment-method", ct);
        foreach (var pm in methods.Items().Where(p => p.Str("deleted_at") is null))
        {
            var id = pm.Str("id")!;
            // A removed payment method can no longer be edited, so scrub the holder's name first.
            await _stax.PutAsync($"payment-method/{id}", new JsonObject { ["person_name"] = Erased }, ct);
            await _stax.DeleteAsync($"payment-method/{id}", ct);
        }

        await _stax.PutAsync($"customer/{customerId}", new JsonObject
        {
            ["firstname"] = Erased,
            ["lastname"] = Erased,
            ["company"] = "",
            ["email"] = "",
            ["phone"] = "",
            ["address_1"] = "",
            ["address_2"] = "",
            ["address_city"] = "",
            ["address_state"] = "",
            ["address_zip"] = "",
            ["address_country"] = "",
            ["notes"] = null,
            ["reference"] = ""
        }, ct);
        await _stax.DeleteAsync($"customer/{customerId}", ct);
    }
}
