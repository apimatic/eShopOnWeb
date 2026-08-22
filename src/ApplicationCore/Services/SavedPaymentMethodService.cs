using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPalGateway;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalGateway payPalGateway)
    {
        _repository = repository;
        _payPalGateway = payPalGateway;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentSource card, CancellationToken cancellationToken = default)
    {
        ValidateCard(card);

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var payPalCustomerId = existing.Select(p => p.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));
        var merchantCustomerId = ToMerchantCustomerId(buyerId);
        var requestId = $"eshop-vault-{merchantCustomerId}-{Guid.NewGuid():N}"[..Math.Min(108, 64)];

        var vaulted = await _payPalGateway.VaultCardAsync(card, merchantCustomerId, payPalCustomerId, requestId, cancellationToken);

        var alreadySaved = existing.FirstOrDefault(p => p.PayPalVaultId == vaulted.VaultId);
        if (alreadySaved is not null)
        {
            return alreadySaved;
        }

        var last4 = vaulted.LastDigits;
        if (string.IsNullOrEmpty(last4) && card.Number.Length >= 4)
        {
            last4 = card.Number[^4..];
        }

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.VaultId,
            vaulted.CustomerId ?? payPalCustomerId,
            last4 ?? string.Empty,
            vaulted.Brand ?? "CARD",
            vaulted.Expiry ?? card.Expiry,
            vaulted.CardholderName ?? card.Name);

        await _repository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (saved is null)
        {
            throw new PaymentException("Saved payment method was not found.", 404);
        }

        try
        {
            await _payPalGateway.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // Already removed from PayPal; still drop the local record.
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }

    private static void ValidateCard(CardPaymentSource card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentException("Card number and expiry are required.");
        }
    }

    private static string ToMerchantCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return "c" + hex[..21];
    }
}
