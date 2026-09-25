using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> repository,
        IPaymentGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveAsync(string buyerId, CardInput card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _gateway.VaultCardAsync(
            new VaultCardCommand(card, CustomerId: null, IdempotencyKey: Guid.NewGuid().ToString("N")), ct);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.Brand,
            vaulted.Last4,
            vaulted.Expiry,
            vaulted.CardholderName ?? card.CardholderName,
            vaulted.CustomerId);

        await _repository.AddAsync(saved, ct);
        _logger.LogInformation($"Shopper {buyerId} saved card {saved.PublicId} ({vaulted.Brand} ****{vaulted.Last4}).");
        return ToView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        var cards = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        return cards.Select(ToView).ToList();
    }

    public async Task<bool> DeleteAsync(string buyerId, string paymentMethodId, CancellationToken ct = default)
    {
        var saved = (await _repository.ListAsync(new SavedPaymentMethodByPublicIdSpec(buyerId, paymentMethodId), ct)).FirstOrDefault();
        if (saved is null)
        {
            return false; // not the caller's card (or does not exist)
        }

        await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultId, ct);
        await _repository.DeleteAsync(saved, ct);
        _logger.LogInformation($"Shopper {buyerId} deleted saved card {saved.PublicId}.");
        return true;
    }

    private static SavedCardView ToView(SavedPaymentMethod p) =>
        new(p.PublicId, p.CardBrand, p.Last4, p.Expiry, p.CardHolderName, p.CreatedAt);
}
