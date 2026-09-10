using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(IRepository<Buyer> buyerRepository, IPayPalGateway payPal, IAppLogger<SavedCardService> logger)
    {
        _buyerRepository = buyerRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // Vault the card at PayPal first; only a safe reference is ever persisted here.
        var vaulted = await _payPal.VaultCardAsync(card, DeriveCustomerId(buyerId), Guid.NewGuid().ToString("N"), ct);

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        var isNewBuyer = buyer is null;
        buyer ??= new Buyer(buyerId);

        var paymentMethod = new PaymentMethod(vaulted.VaultId, alias, vaulted.Brand, vaulted.Last4, vaulted.Expiry);
        buyer.AddPaymentMethod(paymentMethod);

        if (isNewBuyer)
        {
            await _buyerRepository.AddAsync(buyer, ct);
        }
        else
        {
            await _buyerRepository.UpdateAsync(buyer, ct);
        }

        _logger.LogInformation("Saved card for {Buyer}: vault token stored, {Brand} ending {Last4}", buyerId, vaulted.Brand, vaulted.Last4);
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        var pm = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer is null || pm is null)
        {
            throw new ResourceNotFoundException($"Saved card {paymentMethodId} was not found.");
        }

        // Remove from PayPal's vault so it can no longer be used to pay, then from our records.
        await _payPal.DeleteVaultedCardAsync(pm.CardId, ct);
        buyer.RemovePaymentMethod(paymentMethodId);
        await _buyerRepository.UpdateAsync(buyer, ct);
        _logger.LogInformation("Deleted saved card {PaymentMethodId} for {Buyer}", paymentMethodId, buyerId);
    }

    /// <summary>Derives a stable, PayPal-compliant customer id (≤22 chars, [0-9a-zA-Z_-])
    /// from the shopper's identity, so all of a shopper's vaulted cards group under one
    /// PayPal customer without exposing the identity (an email) as the id.</summary>
    private static string DeriveCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(hash); // uppercase, [0-9A-F]
        return hex[..20];
    }
}
