using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<Buyer> _buyers;
    private readonly IPayPalGateway _paypal;

    public PaymentMethodService(IRepository<Buyer> buyers, IPayPalGateway paypal)
    {
        _buyers = buyers;
        _paypal = paypal;
    }

    public async Task<VaultedCardResult> SaveCardAsync(string buyerId, CardPaymentSource card, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new CheckoutException(401, "A signed-in shopper is required.");
        ValidateCard(card);

        var buyer = await GetOrCreateBuyer(buyerId, ct);
        var result = await _paypal.VaultCardAsync(
            new VaultCardCommand(buyerId, buyer.PayPalCustomerId, card),
            idempotencyKey: $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            ct);

        if (!string.IsNullOrEmpty(result.PayPalCustomerId))
            buyer.SetPayPalCustomerId(result.PayPalCustomerId);

        buyer.AddPaymentMethod(result.PaymentTokenId, result.LastDigits, result.Brand, result.Expiry, result.Name);
        await _buyers.UpdateAsync(buyer, ct);
        return result;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new CheckoutException(401, "A signed-in shopper is required.");
        var buyer = await _buyers.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), ct);
        if (buyer is null) return Array.Empty<PaymentMethod>();
        return buyer.PaymentMethods.ToList();
    }

    public async Task DeleteAsync(string buyerId, string paymentMethodId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new CheckoutException(401, "A signed-in shopper is required.");
        if (string.IsNullOrWhiteSpace(paymentMethodId))
            throw new CheckoutException(400, "A payment method id is required.");

        var buyer = await _buyers.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), ct);
        var saved = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer is null || saved is null)
            throw new CheckoutException(404, "Saved payment method not found.");

        try
        {
            await _paypal.DeleteVaultedCardAsync(paymentMethodId, ct);
        }
        catch (CheckoutException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal — still drop the local record.
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyers.UpdateAsync(buyer, ct);
    }

    private async Task<Buyer> GetOrCreateBuyer(string buyerId, CancellationToken ct)
    {
        var buyer = await _buyers.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), ct);
        if (buyer is not null) return buyer;
        buyer = new Buyer(buyerId);
        await _buyers.AddAsync(buyer, ct);
        return buyer;
    }

    private static void ValidateCard(CardPaymentSource card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode))
            throw new CheckoutException(400, "Card number, expiry (YYYY-MM), and security code are required.");
    }
}
