using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _paymentGateway;

    public PaymentMethodService(IRepository<Buyer> buyerRepository, IPaymentGateway paymentGateway)
    {
        _buyerRepository = buyerRepository;
        _paymentGateway = paymentGateway;
    }

    public async Task<PaymentMethod> SaveAsync(
        string buyerId,
        CardPaymentSource card,
        CancellationToken cancellationToken)
    {
        var buyer = await GetOrCreateBuyer(buyerId, cancellationToken);
        var requestId = $"eshop-vault-{buyerId}-{System.Guid.NewGuid():N}";
        var vaulted = await _paymentGateway.SaveCardAsync(
            buyerId, buyer.PayPalCustomerId, card, requestId, cancellationToken);

        if (!string.IsNullOrEmpty(vaulted.PayPalCustomerId))
        {
            buyer.SetPayPalCustomerId(vaulted.PayPalCustomerId);
        }

        var method = buyer.AddPaymentMethod(
            vaulted.PaymentTokenId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry);

        if (buyer.Id == 0)
        {
            await _buyerRepository.AddAsync(buyer, cancellationToken);
        }
        else
        {
            await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        }

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

        return buyer.PaymentMethods.ToList();
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerByIdentitySpecification(buyerId), cancellationToken);
        var method = buyer?.GetPaymentMethod(paymentMethodId);
        if (buyer is null || method is null)
        {
            throw new CheckoutException("The saved card was not found.", 404);
        }

        if (!string.IsNullOrEmpty(method.CardId))
        {
            await _paymentGateway.DeleteVaultedCardAsync(method.CardId, cancellationToken);
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
    }

    private async Task<Buyer> GetOrCreateBuyer(string buyerId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerByIdentitySpecification(buyerId), cancellationToken);
        return buyer ?? new Buyer(buyerId);
    }
}
