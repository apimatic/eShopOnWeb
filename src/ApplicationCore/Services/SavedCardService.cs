using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalGateway _payPalGateway;

    public SavedCardService(IRepository<Buyer> buyerRepository, IPayPalGateway payPalGateway)
    {
        _buyerRepository = buyerRepository;
        _payPalGateway = payPalGateway;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, string alias, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Vault the raw card with PayPal; this app keeps only the token id and a safe descriptor.
        var vaulted = await _payPalGateway.VaultCardAsync(card, buyerId, cancellationToken);

        var displayAlias = string.IsNullOrWhiteSpace(alias)
            ? $"{vaulted.Brand ?? "Card"} ****{vaulted.Last4}"
            : alias.Trim();

        var (buyer, isNew) = await GetOrCreateBuyerAsync(buyerId, cancellationToken);
        var paymentMethod = buyer.AddPaymentMethod(
            new PaymentMethod(displayAlias, vaulted.VaultId, vaulted.Last4, vaulted.Brand, vaulted.Expiry));

        if (isNew)
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
            // Not the caller's card (or does not exist): nothing removed.
            return false;
        }

        if (!string.IsNullOrEmpty(method.CardId))
        {
            await _payPalGateway.DeleteVaultedCardAsync(method.CardId, cancellationToken);
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        return true;
    }

    private async Task<(Buyer buyer, bool isNew)> GetOrCreateBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        return buyer is null ? (new Buyer(buyerId), true) : (buyer, false);
    }
}
