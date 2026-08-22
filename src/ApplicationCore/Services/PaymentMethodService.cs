using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;

    public PaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required.", 401);
        }

        if (card == null)
        {
            throw new PaymentException("Card details are required.", 400);
        }

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId));
        var paypalCustomerId = existing.FirstOrDefault()?.PayPalCustomerId;
        var merchantCustomerId = ToMerchantCustomerId(buyerId);
        var idempotencyKey = $"eshop-vault-{buyerId}-{System.Guid.NewGuid():N}";

        var vaulted = await _payPal.SaveCardAsync(
            merchantCustomerId, paypalCustomerId, card, idempotencyKey, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.PayPalCustomerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.Name);

        await _repository.AddAsync(saved);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId));
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await GetOwnedAsync(buyerId, paymentMethodId, cancellationToken);
        if (saved == null)
        {
            throw new PaymentException("Saved card was not found.", 404);
        }

        try
        {
            await _payPal.DeletePaymentTokenAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal — still remove the local record.
        }

        await _repository.DeleteAsync(saved);
    }

    public Task<SavedPaymentMethod?> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        return _repository.FirstOrDefaultAsync(new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId));
    }

    internal static string ToMerchantCustomerId(string buyerId)
    {
        var allowed = buyerId.Where(c => char.IsLetterOrDigit(c) || "-_.^*$@#".Contains(c)).ToArray();
        var value = new string(allowed);
        if (string.IsNullOrEmpty(value))
        {
            value = "shopper";
        }

        return value.Length <= 64 ? value : value[..64];
    }
}
