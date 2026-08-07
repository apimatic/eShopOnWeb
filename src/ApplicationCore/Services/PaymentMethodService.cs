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

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IBuyerService _buyerService;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<Buyer> buyerRepository,
        IBuyerService buyerService,
        IPaymentGateway paymentGateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _buyerRepository = buyerRepository;
        _buyerService = buyerService;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<SavedCardInfo> SaveCardAsync(
        string buyerId, PaymentCard card, string? alias, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var buyer = await _buyerService.GetOrCreateBuyerAsync(buyerId, cancellationToken);

        var idempotencyKey = Guid.NewGuid().ToString("N");
        var saved = await _paymentGateway.VaultCardAsync(card, buyer.PayPalCustomerId, idempotencyKey, cancellationToken);

        if (!string.IsNullOrEmpty(saved.CustomerId))
        {
            buyer.SetPayPalCustomerId(saved.CustomerId!);
        }

        var effectiveAlias = string.IsNullOrWhiteSpace(alias)
            ? $"{saved.Brand} ****{saved.Last4}"
            : alias!.Trim();

        var paymentMethod = buyer.AddPaymentMethod(
            new PaymentMethod(effectiveAlias, saved.VaultToken, saved.Last4, saved.Brand, saved.Expiry));

        await _buyerRepository.UpdateAsync(buyer, cancellationToken);

        _logger.LogInformation("Saved card {0} for buyer (vault token stored, no card data persisted).", paymentMethod.Id);
        return ToInfo(paymentMethod);
    }

    public async Task<IReadOnlyList<SavedCardInfo>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var buyer = await _buyerService.GetOrCreateBuyerAsync(buyerId, cancellationToken);
        return buyer.PaymentMethods.Select(ToInfo).ToList();
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var buyer = await _buyerService.GetOrCreateBuyerAsync(buyerId, cancellationToken);

        var removed = buyer.RemovePaymentMethod(paymentMethodId);
        if (removed is null)
        {
            return false; // Not this buyer's card (or does not exist).
        }

        // Persist the removal first so the card can no longer be used to pay, even if the remote
        // vault deletion has trouble.
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(removed.CardId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            // The card is already gone from the application; log so the orphaned vault token can be
            // cleaned up out of band. Never rethrow — deletion from the shopper's perspective succeeded.
            _logger.LogWarning("Card {0} removed locally but PayPal vault deletion failed: {1}", paymentMethodId, ex.Message);
        }

        _logger.LogInformation("Deleted saved card {0} for buyer.", paymentMethodId);
        return true;
    }

    private static SavedCardInfo ToInfo(PaymentMethod pm) =>
        new SavedCardInfo(pm.Id, pm.Alias, pm.Brand, pm.Last4, pm.Expiry);
}
