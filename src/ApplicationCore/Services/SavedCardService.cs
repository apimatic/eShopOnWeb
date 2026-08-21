using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _paymentGateway;

    public SavedCardService(IRepository<Buyer> buyerRepository, IPaymentGateway paymentGateway)
    {
        _buyerRepository = buyerRepository;
        _paymentGateway = paymentGateway;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var isNew = buyer is null;
        buyer ??= new Buyer(buyerId);

        var vaulted = await _paymentGateway.VaultCardAsync(
            card, buyer.PayPalCustomerId, Guid.NewGuid().ToString("N"), cancellationToken);

        if (string.IsNullOrEmpty(buyer.PayPalCustomerId) && !string.IsNullOrEmpty(vaulted.PayPalCustomerId))
            buyer.SetPayPalCustomerId(vaulted.PayPalCustomerId);

        var paymentMethod = buyer.AddPaymentMethod(
            new PaymentMethod(vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, alias));

        if (isNew)
            await _buyerRepository.AddAsync(buyer, cancellationToken);
        else
            await _buyerRepository.UpdateAsync(buyer, cancellationToken);

        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);

        return buyer is null
            ? Array.Empty<PaymentMethod>()
            : buyer.PaymentMethods.ToList();
    }

    public async Task<Result> DeleteCardAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);

        var paymentMethod = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer is null || paymentMethod is null)
            return Result.NotFound();

        // Remove from PayPal's vault first so it can no longer be used to pay.
        if (!string.IsNullOrEmpty(paymentMethod.CardId))
            await _paymentGateway.DeleteVaultedCardAsync(paymentMethod.CardId, cancellationToken);

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);

        return Result.Success();
    }
}
