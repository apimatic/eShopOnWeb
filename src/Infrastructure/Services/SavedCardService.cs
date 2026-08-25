using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedCard> _cardRepo;
    private readonly PayPalClient _paypal;

    public SavedCardService(IRepository<SavedCard> cardRepo, PayPalClient paypal)
    {
        _cardRepo = cardRepo;
        _paypal = paypal;
    }

    public async Task<SavedCardResult> SaveCardAsync(string buyerId, SaveCardRequest request)
    {
        var merchantCustomerId = ComputeMerchantCustomerId(buyerId);
        var expiry = $"{request.CardExpiryYear:D4}-{request.CardExpiryMonth:D2}";

        var vaultResponse = await _paypal.CreateVaultTokenAsync(
            cardNumber: request.CardNumber,
            expiry: expiry,
            cvv: request.Cvv,
            cardholderName: request.CardholderName,
            billingCountryCode: request.BillingCountryCode,
            billingPostalCode: request.BillingPostalCode,
            merchantCustomerId: merchantCustomerId);

        var card = vaultResponse.PaymentSource?.Card;
        var lastFour = card?.LastDigits ?? request.CardNumber[^4..];
        var brand = card?.Brand ?? "UNKNOWN";
        var cardExpiry = card?.Expiry ?? expiry;

        var savedCard = new SavedCard(
            buyerId: buyerId,
            vaultTokenId: vaultResponse.Id,
            lastFour: lastFour,
            brand: brand,
            expiry: cardExpiry,
            cardholderName: request.CardholderName);

        await _cardRepo.AddAsync(savedCard);

        return new SavedCardResult(savedCard.Id, lastFour, brand, cardExpiry, request.CardholderName);
    }

    public async Task<List<SavedCardResult>> GetSavedCardsAsync(string buyerId)
    {
        var cards = await _cardRepo.ListAsync(new SavedCardsByBuyerSpec(buyerId));
        return cards.Select(c => new SavedCardResult(c.Id, c.LastFour, c.Brand, c.Expiry, c.CardholderName)).ToList();
    }

    public async Task DeleteSavedCardAsync(int paymentMethodId, string buyerId)
    {
        var card = await _cardRepo.GetByIdAsync(paymentMethodId)
            ?? throw new InvalidOperationException($"Payment method {paymentMethodId} not found.");

        if (card.BuyerId != buyerId)
            throw new UnauthorizedAccessException("Payment method does not belong to the current user.");

        await _paypal.DeleteVaultTokenAsync(card.VaultTokenId);
        await _cardRepo.DeleteAsync(card);
    }

    private static string ComputeMerchantCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
