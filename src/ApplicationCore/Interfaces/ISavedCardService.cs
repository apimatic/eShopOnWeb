using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Flow 2 — saving a card once (in PayPal's vault) so it can be reused for later orders. Full card
/// details are handed to PayPal and never stored in this app's database.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card in PayPal and records a safe reference (token id + brand + last4) for the shopper.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? label,
        CancellationToken cancellationToken = default);
}
