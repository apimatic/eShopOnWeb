using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class AccountService
{
    private readonly PaymentMethodService _methods;
    private readonly OrderPaymentService _orders;
    private readonly UserManager<ApplicationUser> _users;

    public AccountService(PaymentMethodService methods, OrderPaymentService orders, UserManager<ApplicationUser> users)
    {
        _methods = methods;
        _orders = orders;
        _users = users;
    }

    /// <summary>
    /// Closes the shopper's account and erases their data - first at Stax, then in the shop. Stax is done first so a
    /// failure leaves the shop's links in place and closing can simply be tried again.
    /// </summary>
    public async Task<bool> CloseAsync(string email, CancellationToken ct)
    {
        await _orders.EnsureNothingInFlightAsync(email, ct);
        await _methods.EraseAtStaxAsync(email, ct);
        await _orders.EraseLocalAsync(email, ct);

        var user = await _users.FindByNameAsync(email) ?? await _users.FindByEmailAsync(email);
        if (user != null)
        {
            var result = await _users.DeleteAsync(user);
            if (!result.Succeeded) throw new ApiException(500, "The shop account could not be deleted.");
        }
        return true;
    }
}
