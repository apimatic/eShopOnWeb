using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. Cards are tokenised in the PayPal vault; only a safe descriptor
/// plus the token is kept, always scoped to the owning shopper.
/// </summary>
public interface IPaymentMethodService
{
    Task<SavedCard> SaveCardAsync(string buyerIdentity, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerIdentity, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card owned by the shopper. Returns false if no such card exists for them.</summary>
    Task<bool> DeleteCardAsync(string buyerIdentity, int paymentMethodId, CancellationToken cancellationToken = default);
}

/// <summary>A saved card described safely enough for the shopper to recognise it — never full details.</summary>
public record SavedCard(int Id, string? Brand, string Last4, string? ExpiryMonthYear, string? Alias);
