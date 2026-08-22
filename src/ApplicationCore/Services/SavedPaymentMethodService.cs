using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentsGateway _payPal;

    public SavedPaymentMethodService(IRepository<Buyer> buyerRepository, IPayPalPaymentsGateway payPal)
    {
        _buyerRepository = buyerRepository;
        _payPal = payPal;
    }

    public async Task<PaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentDetails card,
        string? alias,
        CancellationToken cancellationToken)
    {
        var buyer = await GetOrCreateBuyer(buyerId, cancellationToken);
        var vaulted = await _payPal.VaultCardAsync(
            merchantCustomerId: buyerId,
            payPalCustomerId: buyer.PayPalCustomerId,
            card: card,
            idempotencyKey: System.Guid.NewGuid().ToString("N"),
            cancellationToken: cancellationToken);

        if (!string.IsNullOrEmpty(vaulted.PayPalCustomerId))
        {
            buyer.SetPayPalCustomerId(vaulted.PayPalCustomerId);
        }

        var method = buyer.AddPaymentMethod(
            vaulted.VaultTokenId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            alias ?? vaulted.Name);

        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerByIdentitySpecification(buyerId), cancellationToken);
        if (buyer is null)
        {
            return System.Array.Empty<PaymentMethod>();
        }

        return new List<PaymentMethod>(buyer.PaymentMethods);
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerByIdentitySpecification(buyerId), cancellationToken);
        var method = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer is null || method is null)
        {
            throw new PaymentException("Saved payment method was not found.", 404);
        }

        if (!string.IsNullOrEmpty(method.CardId))
        {
            await _payPal.DeleteVaultedCardAsync(method.CardId, cancellationToken);
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
    }

    private async Task<Buyer> GetOrCreateBuyer(string buyerId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerByIdentitySpecification(buyerId), cancellationToken);
        if (buyer is not null)
        {
            return buyer;
        }

        buyer = new Buyer(buyerId);
        return await _buyerRepository.AddAsync(buyer, cancellationToken);
    }
}
