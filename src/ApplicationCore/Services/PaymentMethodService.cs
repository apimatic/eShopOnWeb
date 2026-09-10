using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(IRepository<Buyer> buyerRepository, IPayPalPaymentGateway gateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _buyerRepository = buyerRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var buyer = await GetOrCreateBuyerAsync(buyerId, cancellationToken);

        // A stable-ish idempotency key per save request; the caller may retry the same logical save safely.
        var idempotencyKey = $"vault-{buyerId}-{Guid.NewGuid():N}";
        var vaulted = await _gateway.VaultCardAsync(card, buyer.PayPalCustomerId, idempotencyKey, cancellationToken);

        if (!string.IsNullOrEmpty(vaulted.CustomerId))
        {
            buyer.SetPayPalCustomerId(vaulted.CustomerId!);
        }

        var method = new PaymentMethod(vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, vaulted.CardHolderName ?? card.Name, alias);
        buyer.AddPaymentMethod(method);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);

        _logger.LogInformation($"Saved card for {buyerId}: {vaulted.Brand} ending {vaulted.Last4} (vault {vaulted.VaultId}).");
        return method;
    }

    public async Task<IReadOnlyCollection<PaymentMethod>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var method = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer == null || method == null)
        {
            return false; // not the caller's card (or does not exist)
        }

        await _gateway.DeleteVaultedCardAsync(method.VaultId, cancellationToken);
        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);

        _logger.LogInformation($"Removed saved card {paymentMethodId} (vault {method.VaultId}) for {buyerId}.");
        return true;
    }

    private async Task<Buyer> GetOrCreateBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        if (buyer == null)
        {
            buyer = new Buyer(buyerId);
            await _buyerRepository.AddAsync(buyer, cancellationToken);
        }
        return buyer;
    }
}
