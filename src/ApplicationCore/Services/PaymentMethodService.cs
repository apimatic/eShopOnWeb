using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalGateway _payPalGateway;

    public PaymentMethodService(IRepository<Buyer> buyerRepository, IPayPalGateway payPalGateway)
    {
        _buyerRepository = buyerRepository;
        _payPalGateway = payPalGateway;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        if (buyer is null)
        {
            buyer = new Buyer(buyerId);
            await _buyerRepository.AddAsync(buyer, ct);
        }

        var savedCard = await _payPalGateway.SaveCardAsync(buyer.PayPalCustomerId, buyerId, card, ct);

        if (buyer.PayPalCustomerId is null)
        {
            buyer.SetPayPalCustomerId(savedCard.PayPalCustomerId);
        }

        var paymentMethod = buyer.AddPaymentMethod(savedCard.VaultId, savedCard.Brand, savedCard.LastDigits, savedCard.Expiry, savedCard.CardholderName);
        await _buyerRepository.UpdateAsync(buyer, ct);
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListForBuyerAsync(string buyerId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task<PaymentMethod?> GetForBuyerAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        return buyer?.PaymentMethods.FirstOrDefault(p => p.Id == paymentMethodId);
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        var paymentMethod = buyer?.PaymentMethods.FirstOrDefault(p => p.Id == paymentMethodId);
        if (buyer is null || paymentMethod is null)
        {
            return false;
        }

        await _payPalGateway.DeleteSavedCardAsync(paymentMethod.PayPalVaultId, ct);
        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, ct);
        return true;
    }
}
