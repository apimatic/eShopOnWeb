using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _gateway;

    public PaymentMethodService(IRepository<Buyer> buyerRepository, IPayPalPaymentGateway gateway)
    {
        _buyerRepository = buyerRepository;
        _gateway = gateway;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerIdentity, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerIdentity, nameof(buyerIdentity));

        // Tokenise the card in the PCI-compliant vault; we only ever keep the token + a safe descriptor.
        var vaulted = await _gateway.VaultCardAsync(card, cancellationToken);

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerIdentity), cancellationToken);
        var alias = BuildAlias(vaulted.Brand, vaulted.Last4);

        PaymentMethod paymentMethod;
        if (buyer is null)
        {
            buyer = new Buyer(buyerIdentity);
            paymentMethod = buyer.AddPaymentMethod(alias, vaulted.VaultId, vaulted.Last4, vaulted.Brand, vaulted.ExpiryMonthYear);
            await _buyerRepository.AddAsync(buyer, cancellationToken);
        }
        else
        {
            paymentMethod = buyer.AddPaymentMethod(alias, vaulted.VaultId, vaulted.Last4, vaulted.Brand, vaulted.ExpiryMonthYear);
            await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        }

        return ToSavedCard(paymentMethod);
    }

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerIdentity, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerIdentity, nameof(buyerIdentity));

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerIdentity), cancellationToken);
        if (buyer is null)
        {
            return new List<SavedCard>();
        }

        return buyer.PaymentMethods.Select(ToSavedCard).ToList();
    }

    public async Task<bool> DeleteCardAsync(string buyerIdentity, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerIdentity, nameof(buyerIdentity));

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerIdentity), cancellationToken);
        var paymentMethod = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer is null || paymentMethod is null)
        {
            // Not found for this shopper (unknown id, or belongs to someone else).
            return false;
        }

        // Delete from the vault first so a saved card can never be used to pay after removal.
        if (!string.IsNullOrEmpty(paymentMethod.CardId))
        {
            await _gateway.DeleteVaultedCardAsync(paymentMethod.CardId, cancellationToken);
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        return true;
    }

    private static SavedCard ToSavedCard(PaymentMethod pm)
        => new(pm.Id, pm.Brand, pm.Last4 ?? string.Empty, pm.ExpiryMonthYear, pm.Alias);

    private static string BuildAlias(string? brand, string last4)
        => string.IsNullOrWhiteSpace(brand) ? $"Card ending {last4}" : $"{brand} ending {last4}";
}
