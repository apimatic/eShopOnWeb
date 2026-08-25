using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
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

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpec(buyerId), ct);
        if (buyer is null)
        {
            buyer = new Buyer(buyerId);
            await _buyerRepository.AddAsync(buyer, ct);
        }

        var saved = await _gateway.SaveCardAsync(card, buyerId, ct);

        var paymentMethod = new PaymentMethod(buyer.Id, saved.VaultId, saved.Brand, saved.Last4, saved.ExpiryMonth, saved.ExpiryYear, DateTimeOffset.UtcNow);
        buyer.AddPaymentMethod(paymentMethod);

        await _buyerRepository.UpdateAsync(buyer, ct);
        return paymentMethod;
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpec(buyerId), ct);
        var found = buyer?.PaymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);
        if (buyer is null || found is null)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        await _gateway.DeleteSavedCardAsync(found.VaultId, ct);

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, ct);
    }
}
