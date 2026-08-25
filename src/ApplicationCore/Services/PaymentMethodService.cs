using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
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

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias,
        CancellationToken ct = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        if (buyer is null)
        {
            buyer = new Buyer(buyerId);
            await _buyerRepository.AddAsync(buyer, ct);
        }

        var idempotencyKey = $"save-card-{buyerId}-{Guid.NewGuid():N}";
        var vaulted = await _paymentGateway.SaveCardAsync(buyerId, card, idempotencyKey, ct);

        var paymentMethod = buyer.AddPaymentMethod(vaulted.VaultId, vaulted.Brand, vaulted.Last4,
            vaulted.ExpiryYearMonth, alias, DateTimeOffset.UtcNow);

        await _buyerRepository.UpdateAsync(buyer, ct);
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken ct = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct)
            ?? throw new PaymentMethodNotFoundException(paymentMethodId);

        var paymentMethod = buyer.GetPaymentMethod(paymentMethodId);
        await _paymentGateway.DeleteSavedCardAsync(paymentMethod.VaultId, ct);
        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, ct);
    }
}
