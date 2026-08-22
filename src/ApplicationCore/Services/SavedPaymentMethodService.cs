using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _methods;
    private readonly IPaymentGateway _payments;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> methods, IPaymentGateway payments)
    {
        _methods = methods;
        _payments = payments;
    }

    public async Task<SavedCardResult> SaveAsync(string buyerId, CardPaymentSource card, CancellationToken ct)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry)
            || string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new OrderPaymentException("Card details are required to save a payment method.", 400);
        }

        var sanitized = new CardPaymentSource
        {
            Name = card.Name,
            Number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
            Expiry = CardExpiryNormalizer.Normalize(card.Expiry),
            SecurityCode = card.SecurityCode,
            BillingAddress = card.BillingAddress
        };

        var existing = await _methods.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        var paypalCustomerId = existing.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.PayPalCustomerId))?.PayPalCustomerId;
        var requestId = $"eshop-vault-{buyerId}-{Guid.NewGuid():N}";

        var vaulted = await _payments.SaveCardAsync(buyerId, paypalCustomerId, sanitized, requestId, ct);

        var lastDigits = vaulted.LastDigits
            ?? (sanitized.Number.Length >= 4 ? sanitized.Number[^4..] : null);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.PayPalCustomerId ?? paypalCustomerId,
            lastDigits,
            vaulted.Brand,
            vaulted.Expiry ?? sanitized.Expiry,
            vaulted.Name ?? sanitized.Name);

        await _methods.AddAsync(saved, ct);
        return Map(saved);
    }

    public async Task<IReadOnlyList<SavedCardResult>> ListAsync(string buyerId, CancellationToken ct)
    {
        var saved = await _methods.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        return saved.Select(Map).ToList();
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var saved = await _methods.GetByIdAsync(paymentMethodId, ct);
        if (saved is null || !string.Equals(saved.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new OrderPaymentException("Saved payment method not found.", 404);
        }

        try
        {
            await _payments.DeleteCardAsync(saved.PayPalPaymentTokenId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == 404)
        {
            // Already removed at PayPal; still drop the local record.
        }

        await _methods.DeleteAsync(saved, ct);
    }

    private static SavedCardResult Map(SavedPaymentMethod saved) => new()
    {
        PaymentMethodId = saved.Id,
        LastDigits = saved.LastDigits,
        Brand = saved.Brand,
        Expiry = saved.Expiry,
        CardholderName = saved.CardholderName
    };
}
