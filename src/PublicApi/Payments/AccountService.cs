using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Closing a shop account erases the shopper's data in the shop and at Stax.</summary>
public class AccountService
{
    private readonly CatalogContext _db;
    private readonly StaxClient _stax;
    private readonly ShopperContext _shoppers;
    private readonly PaymentMethodService _methods;

    public AccountService(CatalogContext db, StaxClient stax, ShopperContext shoppers, PaymentMethodService methods)
    {
        _db = db;
        _stax = stax;
        _shoppers = shoppers;
        _methods = methods;
    }

    public async Task CloseAsync(string email, CancellationToken ct)
    {
        var inFlight = await _db.ShopperOrders.AnyAsync(o => o.BuyerEmail == email &&
            (o.PaymentState == PaymentState.Pending || o.PaymentState == PaymentState.Processing || o.RefundState == RefundState.Pending), ct);
        if (inFlight)
            throw new ApiRequestException(409, "The account cannot be closed while a payment or refund is still in progress.");

        // Stax first: if it fails the shop data is still there and the shopper can retry.
        var customerIds = new HashSet<string>();
        var linked = await _methods.FindCustomerIdAsync(email, ct);
        if (linked is not null) customerIds.Add(linked);
        foreach (var c in await _stax.FindCustomersByEmailAsync(email, ct)) customerIds.Add(c["id"]!.ToString());

        foreach (var customerId in customerIds)
        {
            foreach (var pm in await _stax.ListAllPaymentMethodsAsync(customerId, ct))
            {
                var id = pm["id"]!.ToString();
                // Stax only accepts edits to methods that are still active, so erase the details first, then remove.
                if (pm["deleted_at"] is null)
                {
                    await ScrubPaymentMethodAsync(id, ct);
                    await _stax.DeletePaymentMethodAsync(id, ct);
                }
            }

            await _stax.UpdateCustomerAsync(customerId, new
            {
                firstname = "Closed",
                lastname = "Account",
                email = $"closed-{customerId}@invalid.example",
                company = "",
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
            await _stax.DeleteCustomerAsync(customerId, ct);
        }

        // The shop: order history is kept for accounting but no longer identifies the shopper.
        var orders = await _db.ShopperOrders.Where(o => o.BuyerEmail == email).ToListAsync(ct);
        var anonymous = $"closed-{Guid.NewGuid():N}@invalid.example";
        foreach (var o in orders) o.BuyerEmail = anonymous;
        var links = await _db.StaxCustomerLinks.Where(l => l.Email == email).ToListAsync(ct);
        _db.StaxCustomerLinks.RemoveRange(links);
        await _db.SaveChangesAsync(ct);

        var user = await _shoppers.FindUserAsync(email);
        if (user is not null)
        {
            var result = await _shoppers.DeleteUserAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("The account could not be removed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private async Task ScrubPaymentMethodAsync(string paymentMethodId, CancellationToken ct)
    {
        try
        {
            await _stax.UpdatePaymentMethodAsync(paymentMethodId, new
            {
                person_name = "Closed Account",
                address_1 = "",
                address_2 = "",
                address_city = "",
                address_state = "",
                address_zip = "",
                address_country = ""
            }, ct);
        }
        catch (StaxException ex) when (ex.IsRejection)
        {
            // Best effort: Stax has no way to edit methods that were removed earlier.
        }
    }
}
