using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
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

    public async Task<PaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var buyer = await GetOrCreateBuyer(buyerId, cancellationToken);
        var vaulted = await _paymentGateway.SaveCardAsync(
            buyerId,
            card,
            Guid.NewGuid().ToString("N"),
            cancellationToken);

        var method = buyer.AddPaymentMethod(
            vaulted.VaultId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.PayPalCustomerId,
            BuildAlias(vaulted));

        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);
        if (buyer is null)
        {
            return Array.Empty<PaymentMethod>();
        }

        return buyer.PaymentMethods.ToList();
    }

    public async Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);
        if (buyer is null)
        {
            throw new PaymentException("Saved payment method was not found.", 404);
        }

        var method = buyer.GetPaymentMethod(paymentMethodId);
        if (method is null)
        {
            throw new PaymentException("Saved payment method was not found.", 404);
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
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);
        if (buyer is not null)
        {
            return buyer;
        }

        buyer = new Buyer(buyerId);
        await _buyerRepository.AddAsync(buyer, cancellationToken);
        return buyer;
    }

    private static string BuildAlias(VaultedCardDetails vaulted)
    {
        var brand = string.IsNullOrWhiteSpace(vaulted.Brand) ? "Card" : vaulted.Brand;
        var last = string.IsNullOrWhiteSpace(vaulted.LastDigits) ? "****" : vaulted.LastDigits;
        return $"{brand} ending {last}";
    }
}
