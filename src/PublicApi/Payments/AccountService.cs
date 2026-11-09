using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public record CloseAccountDto(bool Closed, int PaymentMethodsErased, int StaxCustomersErased, int OrdersErased);

public class AccountService
{
    private readonly CatalogContext _db;
    private readonly StaxGateway _stax;
    private readonly ShopperDirectory _shoppers;
    private readonly UserManager<ApplicationUser> _users;

    public AccountService(CatalogContext db, StaxGateway stax, ShopperDirectory shoppers, UserManager<ApplicationUser> users)
    {
        _db = db;
        _stax = stax;
        _shoppers = shoppers;
        _users = users;
    }

    /// <summary>Erases the shopper at Stax (cards, bank accounts, customer record) and in the shop.</summary>
    public async Task<CloseAccountDto> Close(System.Security.Claims.ClaimsPrincipal principal, CancellationToken ct)
    {
        var email = await _shoppers.RequireShopper(principal);
        var user = await _users.FindByNameAsync(principal.Identity!.Name!);

        var customerIds = await _shoppers.AllCustomerIds(email, ct);

        // Every payment method the shopper ever had, removed ones included.
        var methodIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var customerId in customerIds)
        {
            foreach (var pm in await _stax.ListCustomerPaymentMethods(customerId, ct))
                if (StaxGateway.Str(pm, "id") is { } id) methodIds.Add(id);
            foreach (var t in await _stax.ListCustomerTransactions(customerId, ct))
                if (StaxGateway.Str(t, "payment_method_id") is { Length: > 0 } id) methodIds.Add(id);
        }
        foreach (var id in await _db.SavedPaymentMethods.AsNoTracking()
                     .Where(p => p.BuyerEmail == email || customerIds.Contains(p.StaxCustomerId)).Select(p => p.Id).ToListAsync(ct))
            methodIds.Add(id);

        var erasedMethods = 0;
        foreach (var id in methodIds)
        {
            JsonElement pm;
            try { pm = await _stax.GetPaymentMethod(id, ct); }
            catch (AppException e) when (e.StatusCode == HttpStatusCode.NotFound) { continue; }

            var owner = StaxGateway.Str(pm, "customer_id");
            if (owner == null || !customerIds.Contains(owner, StringComparer.OrdinalIgnoreCase)) continue;

            // Leave nothing personal behind on the method, then remove it.
            try
            {
                await _stax.UpdatePaymentMethod(id, new Dictionary<string, object?>
                {
                    ["person_name"] = "Closed account",
                    ["address_1"] = string.Empty,
                    ["address_2"] = string.Empty,
                    ["address_city"] = string.Empty,
                    ["address_state"] = string.Empty,
                    ["address_zip"] = string.Empty,
                    ["address_country"] = string.Empty
                }, ct);
            }
            catch (AppException) when (StaxGateway.Str(pm, "deleted_at") != null) { /* already removed; best effort */ }

            if (StaxGateway.Str(pm, "deleted_at") == null)
                await _stax.DeletePaymentMethod(id, ct);
            erasedMethods++;
        }

        var erasedCustomers = 0;
        foreach (var customerId in customerIds)
        {
            try
            {
                await _stax.UpdateCustomer(customerId, new Dictionary<string, object?>
                {
                    ["firstname"] = "Closed",
                    ["lastname"] = "Account",
                    ["email"] = string.Empty,
                    ["company"] = string.Empty,
                    ["phone"] = string.Empty,
                    ["address_1"] = string.Empty,
                    ["address_2"] = string.Empty,
                    ["address_city"] = string.Empty,
                    ["address_state"] = string.Empty,
                    ["address_zip"] = string.Empty,
                    ["address_country"] = string.Empty,
                    ["notes"] = string.Empty,
                    ["reference"] = string.Empty,
                    ["cc_emails"] = null,
                    ["cc_sms"] = null
                }, ct);
                await _stax.DeleteCustomer(customerId, ct);
            }
            catch (AppException e) when (e.StatusCode == HttpStatusCode.NotFound) { /* already gone */ }
            erasedCustomers++;
        }

        // In the shop. Orders that moved money stay as anonymous accounting records: a bank payment may still
        // be settling and a refund may still be due, and both are tracked by transaction, not by person.
        var orders = await _db.Orders.Include(o => o.OrderItems).Where(o => o.BuyerId.ToLower() == email).ToListAsync(ct);
        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.OrderPayments.Where(p => p.BuyerEmail == email || orderIds.Contains(p.OrderId)).ToListAsync(ct);
        var anonymous = $"closed-account-{Guid.NewGuid():N}";
        var erasedOrders = 0;
        foreach (var order in orders)
        {
            var payment = payments.FirstOrDefault(p => p.OrderId == order.Id);
            var kept = payment != null && payment.Status != PaymentStatus.Failed;
            if (!kept)
            {
                if (payment != null) _db.OrderPayments.Remove(payment);
                _db.Orders.Remove(order);
                erasedOrders++;
                continue;
            }

            var entry = _db.Entry(order);
            entry.Property(o => o.BuyerId).CurrentValue = anonymous;
            var address = entry.Reference(o => o.ShipToAddress).TargetEntry;
            if (address != null)
                foreach (var name in new[] { "Street", "City", "State", "Country", "ZipCode" })
                    address.Property(name).CurrentValue = string.Empty;
            payment!.BuyerEmail = anonymous;
        }
        _db.SavedPaymentMethods.RemoveRange(await _db.SavedPaymentMethods.Where(p => p.BuyerEmail == email || customerIds.Contains(p.StaxCustomerId)).ToListAsync(ct));
        _db.StaxCustomerLinks.RemoveRange(await _db.StaxCustomerLinks.Where(l => l.BuyerEmail == email).ToListAsync(ct));
        var baskets = await _db.Baskets.Include(b => b.Items).Where(b => b.BuyerId.ToLower() == email).ToListAsync(ct);
        _db.Baskets.RemoveRange(baskets);
        await _db.SaveChangesAsync(ct);

        var deleted = user == null ? IdentityResult.Success : await _users.DeleteAsync(user);
        if (!deleted.Succeeded)
            throw new AppException(HttpStatusCode.InternalServerError, "The account data was erased but the login could not be removed: " + string.Join("; ", deleted.Errors.Select(e => e.Description)));

        return new CloseAccountDto(true, erasedMethods, erasedCustomers, erasedOrders);
    }
}
