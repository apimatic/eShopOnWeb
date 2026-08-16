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

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<Buyer> buyerRepository,
        IPayPalPaymentGateway payPal,
        IAppLogger<SavedCardService> logger)
    {
        _buyerRepository = buyerRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var isNew = buyer is null;
        buyer ??= new Buyer(buyerId);

        // Vault the card in PayPal; only the vault token + a safe descriptor come back here.
        var vaulted = await _payPal.VaultCardAsync(card, buyerId, cancellationToken);

        var paymentMethod = buyer.AddPaymentMethod(
            vaulted.VaultId,
            alias,
            vaulted.Last4 ?? string.Empty,
            vaulted.Brand,
            vaulted.ExpiryMonth,
            vaulted.ExpiryYear);

        if (isNew)
        {
            await _buyerRepository.AddAsync(buyer, cancellationToken);
        }
        else
        {
            await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        }

        _logger.LogInformation("Saved card {0} for buyer.", paymentMethod.Id);
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var paymentMethod = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer is null || paymentMethod is null)
        {
            return false;
        }

        // Remove from PayPal's vault first so the card can no longer be used to pay, then locally.
        if (!string.IsNullOrEmpty(paymentMethod.CardId))
        {
            await _payPal.DeleteVaultedCardAsync(paymentMethod.CardId!, cancellationToken);
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);

        _logger.LogInformation("Deleted saved card {0} for buyer.", paymentMethodId);
        return true;
    }
}
