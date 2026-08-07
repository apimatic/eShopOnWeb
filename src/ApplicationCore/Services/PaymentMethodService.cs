using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves, lists, and removes a shopper's cards. Card data is only ever held in PayPal's vault; this
/// service stores just the vault token and a safe descriptor. Every read/write is scoped to the
/// owning buyer, enforcing that one shopper cannot see, use, or delete another's cards.
/// </summary>
public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPalGateway;

    public PaymentMethodService(IRepository<PaymentMethod> paymentMethodRepository, IPayPalGateway payPalGateway)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _payPalGateway = payPalGateway;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        CardValidation.Validate(card);

        var normalized = CardValidation.NormalizeNumber(card.Number);
        var lastFour = normalized.Length >= 4 ? normalized[^4..] : normalized;
        var customerId = DeriveCustomerId(buyerId);

        // Each save uses a fresh PayPal-Request-Id: vaulting is a create, and a deterministic key
        // would collide with a previously-used id after the same card was saved and later removed
        // (PayPal rejects a reused request id). A dedupe on the returned vault token below still
        // collapses the rare case where PayPal returns an already-known token.
        var idempotencyKey = $"vault-{Guid.NewGuid():N}";

        var vaulted = await _payPalGateway.VaultCardAsync(card, customerId, idempotencyKey, cancellationToken);

        var existing = await _paymentMethodRepository.FirstOrDefaultAsync(
            new PaymentMethodByVaultIdSpecification(buyerId, vaulted.VaultId), cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var paymentMethod = new PaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CardBrand,
            vaulted.LastFourDigits ?? lastFour,
            vaulted.CardholderName ?? card.CardholderName,
            vaulted.Expiry ?? card.Expiry);

        return await _paymentMethodRepository.AddAsync(paymentMethod, cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _paymentMethodRepository.ListAsync(new CustomerPaymentMethodsSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(
            new PaymentMethodForBuyerSpecification(buyerId, paymentMethodId), cancellationToken);
        if (paymentMethod is null)
        {
            return false;
        }

        // Revoke in PayPal first so the card can no longer be charged, then drop the local record.
        await _payPalGateway.RemoveVaultedCardAsync(paymentMethod.VaultId, cancellationToken);
        await _paymentMethodRepository.DeleteAsync(paymentMethod, cancellationToken);
        return true;
    }

    /// <summary>
    /// Derives a stable, PayPal-safe customer id (<c>^[0-9a-zA-Z_-]+$</c>, max 22) from the buyer's
    /// identity so a shopper's vaulted cards are grouped under one PayPal customer.
    /// </summary>
    private static string DeriveCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(hash, 0, 8).ToLowerInvariant(); // 16 chars
        return $"eshop-{hex}"; // 6 + 16 = 22 chars, matches PayPal's constraint
    }
}
