using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<Buyer> buyerRepository, IPayPalPaymentGateway gateway,
        IAppLogger<SavedCardService> logger)
    {
        _buyerRepository = buyerRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string identity, CardDetails card, string? alias,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        Guard.Against.Null(card, nameof(card));

        // Vault the card at PayPal; the raw card never touches this app's database.
        var vaulted = await _gateway.VaultCardAsync(card, cancellationToken);

        var buyer = await GetOrCreateBuyerAsync(identity, cancellationToken);
        var method = buyer.AddPaymentMethod(
            new PaymentMethod(vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, alias));

        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string identity,
        CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerWithPaymentMethodsSpecification(identity), cancellationToken);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task DeleteCardAsync(string identity, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerWithPaymentMethodsSpecification(identity), cancellationToken);

        var method = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer is null || method is null)
        {
            // Either the buyer has no cards, or the card belongs to someone else — don't leak which.
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        // Best-effort removal at PayPal; local removal is what makes it unusable here regardless.
        try
        {
            await _gateway.DeleteVaultedCardAsync(method.VaultId, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning($"Failed to delete PayPal vault token {method.VaultId} (continuing with local removal): {ex.Message}");
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
    }

    private async Task<Buyer> GetOrCreateBuyerAsync(string identity, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerWithPaymentMethodsSpecification(identity), cancellationToken);
        if (buyer is null)
        {
            buyer = await _buyerRepository.AddAsync(new Buyer(identity), cancellationToken);
        }
        return buyer;
    }
}
