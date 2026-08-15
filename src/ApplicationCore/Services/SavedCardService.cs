using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;

    public SavedCardService(IRepository<PaymentMethod> paymentMethodRepository, IPaymentGateway paymentGateway)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Vault the card at PayPal; only the token and a safe descriptor come back — never the number.
        var vaulted = await _paymentGateway.VaultCardAsync(card, buyerId, cancellationToken);

        var paymentMethod = new PaymentMethod(
            buyerId,
            vaulted.VaultTokenId,
            vaulted.CardBrand,
            vaulted.Last4,
            vaulted.ExpiryMonth,
            vaulted.ExpiryYear,
            alias);

        return await _paymentMethodRepository.AddAsync(paymentMethod, cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var spec = new PaymentMethodsByBuyerSpecification(buyerId);
        var cards = await _paymentMethodRepository.ListAsync(spec, cancellationToken);
        return cards;
    }

    public async Task DeleteAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, cancellationToken);

        // Treat "belongs to another shopper" the same as "not found" — never reveal another's card.
        if (paymentMethod is null || paymentMethod.BuyerId != buyerId)
            throw new PaymentMethodNotFoundException(paymentMethodId);

        // Best-effort remove from the PayPal vault, then drop our own record so it can no longer pay.
        await _paymentGateway.DeleteVaultedCardAsync(paymentMethod.CardId, cancellationToken);
        await _paymentMethodRepository.DeleteAsync(paymentMethod, cancellationToken);
    }
}
