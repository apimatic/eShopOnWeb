using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class AccountService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly PaymentsDbContext _db;
    private readonly CatalogContext _catalog;
    private readonly StaxClient _stax;
    private readonly ShopperService _shoppers;

    public AccountService(UserManager<ApplicationUser> users, PaymentsDbContext db, CatalogContext catalog,
        StaxClient stax, ShopperService shoppers)
    {
        _users = users;
        _db = db;
        _catalog = catalog;
        _stax = stax;
        _shoppers = shoppers;
    }

    /// <summary>Closes the shopper's account and erases their data at Stax and in the shop.</summary>
    public async Task CloseAsync(string userName, string email, CancellationToken ct = default)
    {
        var user = await _users.FindByNameAsync(userName)
                   ?? throw new ApiException(401, "This shop account does not exist.");

        var inFlight = await _db.Orders.AnyAsync(o => o.BuyerEmail == email &&
            (o.PaymentState == PaymentState.PENDING || o.PaymentState == PaymentState.PROCESSING ||
             o.RefundStatus == RefundState.PENDING), ct);
        if (inFlight)
            throw ApiException.Conflict("The account cannot be closed while a bank payment is awaiting settlement or a refund is pending.");

        // 1. Stax first: if it fails nothing local is gone and the shopper can simply retry.
        var customerIds = (await _shoppers.FindStaxCustomersAsync(email, ct))
            .Select(c => c["id"]!.GetValue<string>()).ToHashSet();
        var mapped = await _shoppers.FindCustomerIdAsync(email, ct);
        if (mapped != null) customerIds.Add(mapped);

        foreach (var customerId in customerIds)
        {
            List<JsonNode> methods;
            try
            {
                methods = await _stax.GetAllPagesAsync("payment-method",
                    new Dictionary<string, string?> { ["customer_id"] = customerId, ["status"] = "all" }, ct);
            }
            catch (StaxException ex) when (ex.UpstreamStatus == 404) { continue; }

            foreach (var pm in methods.Where(p => p["customer_id"]?.GetValue<string>() == customerId))
            {
                var pmId = pm["id"]!.GetValue<string>();
                if (pm["deleted_at"] == null)
                    await _stax.DeleteAsync($"payment-method/{pmId}", ct);
                // Stax only soft-deletes; blank the personal details that stay on the record.
                await TryAsync(() => _stax.PutAsync($"payment-method/{pmId}", new { person_name = "Closed account" }, ct));
            }

            // Blank the customer's personal details, then delete it.
            await _stax.PutAsync($"customer/{customerId}", new
            {
                firstname = "Closed",
                lastname = "Account",
                company = "",
                email = "",
                phone = "",
                address_1 = "",
                address_2 = "",
                address_city = "",
                address_state = "",
                address_zip = "",
                address_country = "",
                notes = "",
                reference = ""
            }, ct);
            try { await _stax.DeleteAsync($"customer/{customerId}", ct); }
            catch (StaxException ex) when (ex.UpstreamStatus == 404) { }
        }

        // 2. The shop's own data.
        var orders = await _db.Orders.Include(o => o.Lines).Where(o => o.BuyerEmail == email).ToListAsync(ct);
        _db.Orders.RemoveRange(orders);
        var mapping = await _db.Shoppers.Where(s => s.Email == email).ToListAsync(ct);
        _db.Shoppers.RemoveRange(mapping);
        await _db.SaveChangesAsync(ct);

        var buyerIds = new[] { userName, email }.Distinct().ToList();
        var baskets = await _catalog.Baskets.Include(b => b.Items).Where(b => buyerIds.Contains(b.BuyerId)).ToListAsync(ct);
        _catalog.Baskets.RemoveRange(baskets);
        var legacyOrders = await _catalog.Orders.Include(o => o.OrderItems).Where(o => buyerIds.Contains(o.BuyerId)).ToListAsync(ct);
        _catalog.Orders.RemoveRange(legacyOrders);
        await _catalog.SaveChangesAsync(ct);

        var deleted = await _users.DeleteAsync(user);
        if (!deleted.Succeeded)
            throw new ApiException(500, "The account could not be removed: " + string.Join("; ", deleted.Errors.Select(e => e.Description)));
    }

    private static async Task TryAsync(Func<Task> action)
    {
        try { await action(); } catch (StaxException) { }
    }
}
