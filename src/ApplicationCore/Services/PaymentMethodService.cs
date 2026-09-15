using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalClient _payPal;

    public PaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalClient payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Link the new card to the shopper's existing PayPal customer, if they already have one.
        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var customerId = existing
            .Select(pm => pm.PayPalCustomerId)
            .FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var vaulted = await _payPal.VaultCardAsync(card, customerId, cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.CardBrand, vaulted.LastFourDigits,
            vaulted.Expiry, vaulted.CardholderName ?? card.CardholderName, vaulted.CustomerId ?? customerId);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return cards.OrderByDescending(c => c.CreatedAt).ToList();
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var card = await _repository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (card is null || card.BuyerId != buyerId)
            return false; // a shopper can only delete their own card

        // Remove the vaulted card at PayPal too, so it can never be used again. An already-removed
        // token (404) is fine; any other failure is surfaced.
        try
        {
            await _payPal.DeleteVaultedCardAsync(card.VaultId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            // already gone at PayPal; proceed to remove locally
        }

        await _repository.DeleteAsync(card, cancellationToken);
        return true;
    }
}
