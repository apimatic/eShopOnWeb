using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
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

    public async Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card, string? alias)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId));
        if (buyer is null)
        {
            buyer = await _buyerRepository.AddAsync(new Buyer(buyerId));
        }

        var requestId = $"vault-{buyer.PayPalCustomerId}-{Guid.NewGuid():N}";
        var vaulted = await _paymentGateway.VaultCardAsync(requestId, buyer.PayPalCustomerId, card);

        var paymentMethod = buyer.AddPaymentMethod(vaulted.VaultId, vaulted.CardBrand, vaulted.Last4, vaulted.Expiry, alias);
        await _buyerRepository.UpdateAsync(buyer);
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId));
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task<bool> DeletePaymentMethodAsync(string buyerId, int paymentMethodId)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId));
        var method = buyer?.PaymentMethods.FirstOrDefault(m => m.Id == paymentMethodId);
        if (buyer is null || method is null)
        {
            return false;
        }

        await _paymentGateway.DeleteVaultedCardAsync(method.VaultId);
        buyer.RemovePaymentMethod(method);
        await _buyerRepository.UpdateAsync(buyer);
        return true;
    }
}
