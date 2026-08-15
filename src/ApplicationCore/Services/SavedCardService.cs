using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Saves, lists and removes a shopper's cards. Only the PayPal vault token and a safe descriptor are
/// stored; raw card details flow to PayPal and are never persisted here. Every operation is scoped to
/// the calling shopper's own <see cref="Buyer"/>.
/// </summary>
public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalVaultGateway _vaultGateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<Buyer> buyerRepository,
        IPayPalVaultGateway vaultGateway,
        IAppLogger<SavedCardService> logger)
    {
        _buyerRepository = buyerRepository;
        _vaultGateway = vaultGateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var isNew = buyer is null;
        buyer ??= new Buyer(buyerId);

        var request = new VaultCardRequest(
            Card: card,
            MerchantCustomerId: buyer.PayPalCustomerId is null ? ToMerchantCustomerId(buyerId) : null,
            PayPalCustomerId: buyer.PayPalCustomerId,
            Alias: alias,
            IdempotencyKey: Guid.NewGuid().ToString("N"));

        var vaulted = await _vaultGateway.VaultCardAsync(request, cancellationToken);

        if (!string.IsNullOrEmpty(vaulted.PayPalCustomerId))
        {
            buyer.SetPayPalCustomerId(vaulted.PayPalCustomerId);
        }

        var paymentMethod = buyer.AddPaymentMethod(new PaymentMethod(
            vaultId: vaulted.VaultId,
            brand: vaulted.Brand,
            last4: vaulted.Last4,
            expiry: vaulted.Expiry,
            cardholderName: vaulted.CardholderName ?? card.CardholderName,
            alias: alias));

        if (isNew)
        {
            await _buyerRepository.AddAsync(buyer, cancellationToken);
        }
        else
        {
            await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        }

        _logger.LogInformation("Saved card {0} ({1} ****{2}) for a shopper.", paymentMethod.Id, vaulted.Brand, vaulted.Last4);
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var card = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer is null || card is null)
        {
            return false; // not the caller's card, or no such card
        }

        try
        {
            await _vaultGateway.DeleteVaultedCardAsync(card.VaultId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal — proceed to remove locally so it disappears from the shopper's list.
            _logger.LogWarning("Vaulted card {0} was already absent at PayPal on delete.", paymentMethodId);
        }

        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        _logger.LogInformation("Removed saved card {0} for a shopper.", paymentMethodId);
        return true;
    }

    /// <summary>Maps a shopper identity to a PayPal merchant_customer_id (pattern ^[0-9a-zA-Z-_.^*$@#]+$, 1..64).</summary>
    private static string ToMerchantCustomerId(string identity)
    {
        var sb = new StringBuilder(identity.Length);
        foreach (var ch in identity)
        {
            var ok = char.IsLetterOrDigit(ch) || "-_.^*$@#".IndexOf(ch) >= 0;
            sb.Append(ok ? ch : '-');
        }
        var result = sb.ToString();
        if (result.Length == 0) result = "shopper";
        return result.Length > 64 ? result.Substring(0, 64) : result;
    }
}
