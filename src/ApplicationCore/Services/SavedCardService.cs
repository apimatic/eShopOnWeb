using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<Buyer> buyerRepository, IPayPalClient payPal, IAppLogger<SavedCardService> logger)
    {
        _buyerRepository = buyerRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, string? alias, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Vault the card with PayPal first; the raw number never touches our database.
        var vaulted = await _payPal.VaultCardAsync(card, cancellationToken);

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        PaymentMethod method;
        if (buyer is null)
        {
            buyer = new Buyer(buyerId);
            method = buyer.AddPaymentMethod(vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, alias);
            await _buyerRepository.AddAsync(buyer, cancellationToken);
        }
        else
        {
            method = buyer.AddPaymentMethod(vaulted.VaultId, vaulted.Brand, vaulted.Last4, vaulted.Expiry, alias);
            await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        }

        _logger.LogInformation($"Saved card {method.Id} ({vaulted.Brand} ****{vaulted.Last4}) for {buyerId}, vault {vaulted.VaultId}.");
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        return buyer is null
            ? new List<PaymentMethod>()
            : buyer.PaymentMethods.OrderByDescending(pm => pm.CreatedAt).ToList();
    }

    public async Task RemoveCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        // Only the caller's own Buyer is loaded, so one shopper can never delete another's card.
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var method = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer is null || method is null)
            throw new EntityNotFoundException("Saved card", paymentMethodId);

        var vaultId = method.PayPalVaultId;

        // Remove locally first so the card can no longer be used to pay, even if the PayPal
        // delete has already happened or the token is already gone.
        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);

        try
        {
            await _payPal.DeletePaymentTokenAsync(vaultId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.PayPalStatusCode == 404)
        {
            // Already deleted at PayPal; nothing more to do.
            _logger.LogInformation($"PayPal vault token {vaultId} was already gone when removing card {paymentMethodId}.");
        }

        _logger.LogInformation($"Removed saved card {paymentMethodId} (vault {vaultId}) for {buyerId}.");
    }
}
