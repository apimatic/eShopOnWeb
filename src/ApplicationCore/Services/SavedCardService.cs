using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _gateway;

    public SavedCardService(IRepository<Buyer> buyerRepository, IPaymentGateway gateway)
    {
        _buyerRepository = buyerRepository;
        _gateway = gateway;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        if (buyer is null)
        {
            buyer = new Buyer(buyerId);
            await _buyerRepository.AddAsync(buyer, ct);
        }

        var result = await _gateway.SaveCardAsync(card, Guid.NewGuid().ToString(), ct);

        var paymentMethod = buyer.AddPaymentMethod(result.VaultId, result.Brand, result.Last4, result.Expiry);
        await _buyerRepository.UpdateAsync(buyer, ct);

        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetSavedCardsAsync(string buyerId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        return buyer?.PaymentMethods.Where(p => p.IsActive).ToList() ?? new List<PaymentMethod>();
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        var method = buyer?.PaymentMethods.FirstOrDefault(p => p.Id == paymentMethodId && p.IsActive);
        if (method is null)
        {
            throw new ResourceNotFoundException($"Saved card {paymentMethodId} was not found.");
        }

        await _gateway.DeleteSavedCardAsync(method.CardId!, ct);

        buyer!.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, ct);
    }
}
