using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IBuyerService _buyerService;
    private readonly IPaymentGateway _paymentGateway;

    public SavedCardService(
        IRepository<Buyer> buyerRepository,
        IBuyerService buyerService,
        IPaymentGateway paymentGateway)
    {
        _buyerRepository = buyerRepository;
        _buyerService = buyerService;
        _paymentGateway = paymentGateway;
    }

    public async Task<PaymentMethod> SaveCardAsync(
        string identity, CardDetails card, string? alias, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        Guard.Against.Null(card, nameof(card));

        // Tokenise the card in PayPal's vault first; only the returned token + safe summary are stored.
        var vaulted = await _paymentGateway.VaultCardAsync(card, Guid.NewGuid().ToString(), cancellationToken);

        var buyer = await _buyerService.GetOrCreateBuyerAsync(identity, cancellationToken);
        var paymentMethod = buyer.AddPaymentMethod(vaulted.VaultId, vaulted.CardBrand, vaulted.Last4, vaulted.Expiry, alias);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);

        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetSavedCardsAsync(
        string identity, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerService.GetBuyerAsync(identity, cancellationToken);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task<bool> DeleteSavedCardAsync(
        string identity, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerService.GetBuyerAsync(identity, cancellationToken);
        var paymentMethod = buyer?.GetPaymentMethod(paymentMethodId);
        if (buyer is null || paymentMethod is null)
        {
            return false;
        }

        var vaultId = paymentMethod.VaultId;

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);

        // Best-effort: also remove the token from PayPal. The card is already unusable in our app.
        await _paymentGateway.DeleteVaultedCardAsync(vaultId, cancellationToken);

        return true;
    }
}
