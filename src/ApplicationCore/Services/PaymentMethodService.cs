using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _payPal;

    public PaymentMethodService(IRepository<Buyer> buyerRepository, IPayPalPaymentGateway payPal)
    {
        _buyerRepository = buyerRepository;
        _payPal = payPal;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var isNewBuyer = buyer is null;
        buyer ??= new Buyer(buyerId);

        // Vault the card in PayPal, grouping it under this shopper's PayPal customer id (if we have one).
        var vaulted = await _payPal.VaultCardAsync(card, buyer.PayPalCustomerId, cancellationToken);

        if (string.IsNullOrEmpty(buyer.PayPalCustomerId) && !string.IsNullOrEmpty(vaulted.CustomerId))
        {
            buyer.SetPayPalCustomerId(vaulted.CustomerId!);
        }

        var paymentMethod = new PaymentMethod(vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, alias);
        buyer.AddPaymentMethod(paymentMethod);

        if (isNewBuyer)
        {
            await _buyerRepository.AddAsync(buyer, cancellationToken);
        }
        else
        {
            await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        }

        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        return buyer is null
            ? new List<PaymentMethod>()
            : buyer.PaymentMethods.ToList();
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var method = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer is null || method is null)
        {
            return false;
        }

        // Remove from PayPal's vault first so a saved card can never be used to pay after deletion.
        await _payPal.DeleteVaultedCardAsync(method.VaultId, cancellationToken);
        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        return true;
    }
}
