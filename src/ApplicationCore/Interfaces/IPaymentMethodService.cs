using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Raw card + billing details to save. Never stored or logged; only vaulted at PayPal.</summary>
public record SaveCardCommand(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);

/// <summary>
/// Manages a shopper's saved cards (Flow 2). Cards are vaulted at PayPal; this app keeps only the
/// vault token and a safe description. All operations are scoped to the owning shopper.
/// </summary>
public interface IPaymentMethodService
{
    Task<SavedCard> SaveCardAsync(string buyerId, SaveCardCommand command, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default);
}
