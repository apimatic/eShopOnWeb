using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalVaultGateway _vault;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<SavedPaymentMethod> repository, IPayPalVaultGateway vault,
        IAppLogger<SavedCardService> logger)
    {
        _repository = repository;
        _vault = vault;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentConflictException("A card number and expiry are required to save a card.");
        }

        var customerId = DeriveCustomerId(buyerId);
        var idempotencyKey = $"vault-{customerId}-{Guid.NewGuid():N}";

        var vaulted = await _vault.VaultCardAsync(customerId, card, idempotencyKey, ct);

        var saved = new SavedPaymentMethod(buyerId, vaulted.PayPalCustomerId, vaulted.VaultId,
            vaulted.Brand, vaulted.LastDigits, vaulted.CardholderName ?? card.CardholderName, vaulted.Expiry ?? card.Expiry);
        saved = await _repository.AddAsync(saved, ct);

        _logger.LogInformation($"Saved a {saved.Brand} card ending {saved.LastDigits} for buyer {buyerId} (vault {saved.VaultId}).");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetCardsForBuyerAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Scoped to the caller: another shopper's card simply isn't found here.
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdForBuyerSpec(paymentMethodId, buyerId), ct)
            ?? throw new PaymentNotFoundException($"Saved card {paymentMethodId} was not found for this shopper.");

        try
        {
            await _vault.DeletePaymentTokenAsync(saved.VaultId, ct);
        }
        catch (PayPalApiException ex) when (ex.PayPalStatusCode == 404)
        {
            // Already gone at PayPal; removing our record still satisfies the outcome.
            _logger.LogWarning($"PayPal vault token {saved.VaultId} was already absent when deleting card {paymentMethodId}.");
        }

        await _repository.DeleteAsync(saved, ct);
        _logger.LogInformation($"Removed saved card {paymentMethodId} (vault {saved.VaultId}) for buyer {buyerId}.");
    }

    /// <summary>
    /// A stable PayPal customer id per shopper, so a shopper's vaulted cards group under one customer.
    /// Derived from the buyer id; contains only characters PayPal's customer id accepts.
    /// </summary>
    private static string DeriveCustomerId(string buyerId)
    {
        // PayPal's merchant customer id is capped at 22 chars ([0-9a-zA-Z_-]); keep well under.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(hash, 0, 7).ToLowerInvariant(); // 14 hex chars
        return $"eshop-{hex}"; // 20 chars total
    }
}
