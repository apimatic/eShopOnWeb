using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentProcessor _payments;

    public SavedPaymentMethodService(IRepository<Buyer> buyerRepository, IPaymentProcessor payments)
    {
        _buyerRepository = buyerRepository;
        _payments = payments;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardPaymentInput card, string? alias, CancellationToken ct)
    {
        var buyer = await GetOrCreateBuyer(buyerId, ct);
        var requestId = $"eshop-vault-{buyerId}-{Guid.NewGuid():N}";
        var vaulted = await _payments.VaultCardAsync(buyerId, buyer.PayPalCustomerId, card, requestId, ct);

        if (!string.IsNullOrEmpty(vaulted.PayPalCustomerId))
            buyer.SetPayPalCustomerId(vaulted.PayPalCustomerId);

        var method = buyer.AddPaymentMethod(vaulted.PaymentTokenId, vaulted.LastDigits, vaulted.Brand, vaulted.Expiry, alias);
        await _buyerRepository.UpdateAsync(buyer, ct);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), ct);
        if (buyer == null)
            return Array.Empty<PaymentMethod>();
        return buyer.PaymentMethods.ToList();
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), ct);
        if (buyer == null)
            throw new EntityNotFoundException($"Payment method {paymentMethodId} was not found.");

        var method = buyer.GetPaymentMethod(paymentMethodId);
        if (method == null)
            throw new EntityNotFoundException($"Payment method {paymentMethodId} was not found.");

        if (!string.IsNullOrEmpty(method.CardId))
        {
            try
            {
                await _payments.DeleteVaultedCardAsync(method.CardId, ct);
            }
            catch (PaymentProcessingException ex) when (ex.StatusCode == 404)
            {
                // Already gone at PayPal — still drop the local record.
            }
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, ct);
    }

    private async Task<Buyer> GetOrCreateBuyer(string buyerId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), ct);
        if (buyer != null)
            return buyer;

        buyer = new Buyer(buyerId);
        return await _buyerRepository.AddAsync(buyer, ct);
    }
}
