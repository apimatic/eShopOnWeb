using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<PaymentMethod> _repository;
    private readonly IPayPalGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<PaymentMethod> repository,
        IPayPalGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var merchantCustomerId = DeriveMerchantCustomerId(buyerId);
        var idempotencyKey = $"eshop-vault-{Guid.NewGuid():N}";

        var saved = await _gateway.VaultCardAsync(merchantCustomerId, card, idempotencyKey, ct);

        var method = new PaymentMethod(
            buyerId, saved.VaultId, saved.CustomerId, saved.Brand, saved.LastFourDigits, saved.Expiry);
        method = await _repository.AddAsync(method, ct);

        _logger.LogInformation("Saved card {0} ({1} ****{2}) for {3}.",
            method.Id, saved.Brand, saved.LastFourDigits, buyerId);
        return ToView(method);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetCardsForBuyerAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _repository.ListAsync(new PaymentMethodsByBuyerSpecification(buyerId), ct);
        return cards.OrderByDescending(c => c.CreatedDate).Select(ToView).ToList();
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var method = await _repository.GetByIdAsync(paymentMethodId, ct);
        if (method is null || method.BuyerId != buyerId)
            throw new PaymentMethodNotFoundException(paymentMethodId);

        // Remove from PayPal first so the card can no longer be used to pay, then locally.
        await _gateway.DeleteVaultedCardAsync(method.PayPalVaultId, ct);
        await _repository.DeleteAsync(method, ct);

        _logger.LogInformation("Deleted saved card {0} for {1}.", paymentMethodId, buyerId);
    }

    private static SavedCardView ToView(PaymentMethod method) =>
        new(method.Id, method.CardBrand, method.LastFourDigits, method.Expiry, method.CreatedDate);

    /// <summary>
    /// A stable, PayPal-safe customer id derived from the shopper so all their saved cards group under
    /// one customer. Deterministic per shopper; carries nothing sensitive.
    /// </summary>
    private static string DeriveMerchantCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var token = Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
        return $"eshop{token}";
    }
}
