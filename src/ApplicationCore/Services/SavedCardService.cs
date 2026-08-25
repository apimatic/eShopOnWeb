using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalGateway _payPalGateway;

    public SavedCardService(IRepository<Buyer> buyerRepository, IPayPalGateway payPalGateway)
    {
        _buyerRepository = buyerRepository;
        _payPalGateway = payPalGateway;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        if (buyer is null)
        {
            buyer = await _buyerRepository.AddAsync(new Buyer(buyerId), ct);
        }

        var requestId = $"paypal-vault-card-{buyerId}-{Guid.NewGuid():N}";
        var result = await _payPalGateway.CreateVaultTokenAsync(requestId, card, ct);

        var paymentMethod = new PaymentMethod(buyer.Id, result.VaultId, result.Brand, result.LastDigits, result.Expiry);
        buyer.AddPaymentMethod(paymentMethod);
        await _buyerRepository.UpdateAsync(buyer, ct);
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetSavedCardsAsync(string buyerId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task<bool> DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        var method = buyer?.PaymentMethods.FirstOrDefault(m => m.Id == paymentMethodId);
        if (buyer is null || method is null) return false;

        await _payPalGateway.DeleteVaultTokenAsync(method.VaultId, ct);
        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, ct);
        return true;
    }
}
