using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _repository;
    private readonly IPayPalGateway _payPal;

    public SavedPaymentMethodService(IRepository<SavedPaymentMethod> repository, IPayPalGateway payPal)
    {
        _repository = repository;
        _payPal = payPal;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentSource card,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentDomainException("The caller identity is required.", HttpStatusCode.Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) ||
            string.IsNullOrWhiteSpace(card.SecurityCode) ||
            string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentDomainException("Card number, expiry, security code, and name are required.");
        }

        var existing = await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var payPalCustomerId = existing.Count > 0 ? existing[0].PayPalCustomerId : null;

        var vaulted = await _payPal.VaultCardAsync(
            card,
            buyerId,
            payPalCustomerId,
            $"eshop-vault-{buyerId}-{System.Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.CustomerId ?? payPalCustomerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.Name ?? card.Name);

        return await _repository.AddAsync(saved, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _repository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _repository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (saved == null)
        {
            var otherShopper = await _repository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpec(paymentMethodId), cancellationToken);
            if (otherShopper != null)
            {
                throw new PaymentDomainException("The caller cannot act on another shopper's payment method.", HttpStatusCode.Forbidden);
            }

            throw new SavedPaymentMethodNotFoundException(paymentMethodId);
        }

        try
        {
            await _payPal.DeletePaymentTokenAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already removed at PayPal; still drop the local record.
        }

        await _repository.DeleteAsync(saved, cancellationToken);
    }
}
