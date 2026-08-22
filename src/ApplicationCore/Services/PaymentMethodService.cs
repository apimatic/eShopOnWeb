using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalGateway _payPal;

    public PaymentMethodService(IRepository<Buyer> buyerRepository, IPayPalGateway payPal)
    {
        _buyerRepository = buyerRepository;
        _payPal = payPal;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardPayment card, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(card.Number)
            || string.IsNullOrWhiteSpace(card.Expiry)
            || string.IsNullOrWhiteSpace(card.SecurityCode)
            || string.IsNullOrWhiteSpace(card.Name))
        {
            throw new CheckoutException(400, "Card number, expiry, security code, and name are required.");
        }

        var buyer = await GetOrCreateBuyerAsync(buyerId, cancellationToken);
        var vaulted = await _payPal.SaveCardAsync(
            card,
            buyer.PayPalCustomerId,
            $"vault-{buyerId}-{Guid.NewGuid():N}",
            cancellationToken);

        if (string.IsNullOrWhiteSpace(vaulted.VaultId) || string.IsNullOrWhiteSpace(vaulted.LastDigits))
        {
            throw new CheckoutException(502, "PayPal did not return a vaulted payment token.");
        }

        if (!string.IsNullOrWhiteSpace(vaulted.CustomerId))
        {
            buyer.SetPayPalCustomerId(vaulted.CustomerId);
        }

        var method = buyer.AddPaymentMethod(
            vaulted.VaultId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.CardholderName);

        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);
        if (buyer is null)
        {
            return Array.Empty<PaymentMethod>();
        }

        return buyer.PaymentMethods.ToList();
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);
        var method = buyer?.GetPaymentMethod(paymentMethodId);
        if (buyer is null || method is null)
        {
            throw new CheckoutException(404, $"Payment method {paymentMethodId} was not found.");
        }

        if (!string.IsNullOrWhiteSpace(method.CardId))
        {
            await _payPal.DeleteVaultedCardAsync(method.CardId, cancellationToken);
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
    }

    private async Task<Buyer> GetOrCreateBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);
        if (buyer is not null)
        {
            return buyer;
        }

        buyer = new Buyer(buyerId);
        await _buyerRepository.AddAsync(buyer, cancellationToken);
        return buyer;
    }
}
