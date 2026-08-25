using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedPaymentMethodService : ISavedPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _gateway;

    public SavedPaymentMethodService(IRepository<Buyer> buyerRepository, IPayPalPaymentGateway gateway)
    {
        _buyerRepository = buyerRepository;
        _gateway = gateway;
    }

    public async Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, PayPalCardDetails card, string? alias, CancellationToken ct = default)
    {
        var buyer = await GetOrCreateBuyerAsync(buyerId, ct);

        var requestId = $"eshop-buyer-{buyer.Id}-savecard-{Guid.NewGuid():N}";
        var vaulted = await _gateway.SaveCardAsync(card, buyerId, requestId, ct);

        var paymentMethod = buyer.AddPaymentMethod(vaulted.VaultId, alias, vaulted.Brand, vaulted.Last4, vaulted.Expiry, vaulted.CardType);
        await _buyerRepository.UpdateAsync(buyer, ct);
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListPaymentMethodsAsync(string buyerId, CancellationToken ct = default)
    {
        var spec = new BuyerWithPaymentMethodsSpecification(buyerId);
        var buyer = await _buyerRepository.FirstOrDefaultAsync(spec, ct);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var spec = new BuyerWithPaymentMethodsSpecification(buyerId);
        var buyer = await _buyerRepository.FirstOrDefaultAsync(spec, ct);
        var method = buyer?.PaymentMethods.FirstOrDefault(p => p.Id == paymentMethodId);
        if (buyer is null || method is null)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        try
        {
            await _gateway.DeleteSavedCardAsync(method.PayPalVaultId, ct);
        }
        catch (PayPalGatewayException ex) when (ex.HttpStatusCode == 404)
        {
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, ct);
    }

    private async Task<Buyer> GetOrCreateBuyerAsync(string buyerId, CancellationToken ct)
    {
        var spec = new BuyerWithPaymentMethodsSpecification(buyerId);
        var buyer = await _buyerRepository.FirstOrDefaultAsync(spec, ct);
        if (buyer is not null)
        {
            return buyer;
        }

        return await _buyerRepository.AddAsync(new Buyer(buyerId), ct);
    }
}
