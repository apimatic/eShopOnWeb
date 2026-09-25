using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. A saved card belongs to the shopper who saved it;
/// full card details are vaulted with PayPal and never stored here.
/// </summary>
public sealed class PaymentMethodService : IPaymentMethodService
{
    private readonly IRepository<SavedPaymentMethod> _cardRepository;
    private readonly IRepository<PayPalCustomerRef> _customerRepository;
    private readonly IPayPalGateway _gateway;
    private readonly ILogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<SavedPaymentMethod> cardRepository,
        IRepository<PayPalCustomerRef> customerRepository,
        IPayPalGateway gateway,
        ILogger<PaymentMethodService> logger)
    {
        _cardRepository = cardRepository;
        _customerRepository = customerRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse the shopper's PayPal customer id if we have one, so all their cards group together.
        var existing = (await _customerRepository.ListAsync(
            new PayPalCustomerRefByBuyerSpec(buyerId), cancellationToken)).FirstOrDefault();

        var vaulted = await _gateway.VaultCardAsync(
            new PayPalVaultCardRequest(card, existing?.PayPalCustomerId), cancellationToken);

        // First vault for this shopper: persist the PayPal-generated customer id for later reuse.
        if (existing is null && vaulted.PayPalCustomerId is not null)
        {
            await _customerRepository.AddAsync(
                new PayPalCustomerRef(buyerId, vaulted.PayPalCustomerId), cancellationToken);
        }

        var saved = new SavedPaymentMethod(buyerId, vaulted.PaymentTokenId, vaulted.Brand,
            vaulted.LastFourDigits, vaulted.Expiry, vaulted.CardholderName);
        await _cardRepository.AddAsync(saved, cancellationToken);

        _logger.LogInformation("Saved card {CardId} ({Brand} ****{Last4}) for {BuyerId}.",
            saved.Id, vaulted.Brand, vaulted.LastFourDigits, buyerId);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetCardsAsync(string buyerId,
        CancellationToken cancellationToken)
        => await _cardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);

    public async Task<bool> DeleteCardAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken)
    {
        var saved = (await _cardRepository.ListAsync(
            new SavedPaymentMethodByIdSpec(paymentMethodId, buyerId), cancellationToken)).FirstOrDefault();
        if (saved is null) return false; // not found or not owned by the caller

        // Remove from PayPal's vault first so the card can no longer be used to pay, then locally.
        await _gateway.DeleteVaultedCardAsync(saved.PayPalPaymentTokenId, cancellationToken);
        await _cardRepository.DeleteAsync(saved, cancellationToken);

        _logger.LogInformation("Deleted saved card {CardId} for {BuyerId}.", paymentMethodId, buyerId);
        return true;
    }
}
