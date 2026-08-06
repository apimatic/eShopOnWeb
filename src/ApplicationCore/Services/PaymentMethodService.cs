using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentMethodService : IPaymentMethodService
{
    // Serialise saves per shopper so a double-submit reuses one PayPal customer id.
    private static readonly KeyedAsyncLock BuyerLocks = new();

    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IAppLogger<PaymentMethodService> _logger;

    public PaymentMethodService(
        IRepository<Buyer> buyerRepository,
        IPayPalGateway payPalGateway,
        IAppLogger<PaymentMethodService> logger)
    {
        _buyerRepository = buyerRepository;
        _payPalGateway = payPalGateway;
        _logger = logger;
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        using (await BuyerLocks.LockAsync(buyerId, cancellationToken))
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId));

            // Deterministic idempotency key from non-secret parts (last four + expiry) so a
            // rapid double-submit of the same card does not create two vault tokens. The full
            // PAN is never used to build the key.
            var last4 = SafeLast4(card.Number);
            var idempotencyKey = BuildVaultIdempotencyKey(buyerId, last4, card.Expiry);

            var vaulted = await _payPalGateway.VaultCardAsync(card, buyer?.PayPalCustomerId, idempotencyKey, cancellationToken);

            var paymentMethod = new PaymentMethod(
                cardId: vaulted.VaultToken,
                last4: vaulted.Last4 ?? last4,
                brand: vaulted.Brand ?? string.Empty,
                expiry: vaulted.Expiry ?? card.Expiry);

            if (buyer is null)
            {
                buyer = new Buyer(buyerId);
                buyer.SetPayPalCustomerId(vaulted.CustomerId);
                buyer.AddPaymentMethod(paymentMethod);
                await _buyerRepository.AddAsync(buyer);
            }
            else
            {
                if (string.IsNullOrEmpty(buyer.PayPalCustomerId))
                {
                    buyer.SetPayPalCustomerId(vaulted.CustomerId);
                }
                buyer.AddPaymentMethod(paymentMethod);
                await _buyerRepository.UpdateAsync(buyer);
            }

            _logger.LogInformation($"Saved card (vault token {vaulted.VaultToken}) for shopper under PayPal customer {vaulted.CustomerId}.");
            return paymentMethod;
        }
    }

    public async Task<IReadOnlyCollection<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId));
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        using (await BuyerLocks.LockAsync(buyerId, cancellationToken))
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId));
            var paymentMethod = buyer?.PaymentMethods.FirstOrDefault(pm => pm.Id == paymentMethodId);

            // Only ever act on a card owned by the caller; otherwise report "not found".
            if (buyer is null || paymentMethod is null)
            {
                return false;
            }

            // Revoke at PayPal first so the card can no longer be charged, then drop it locally.
            if (!string.IsNullOrEmpty(paymentMethod.CardId))
            {
                await _payPalGateway.DeleteVaultedCardAsync(paymentMethod.CardId!, cancellationToken);
            }

            buyer.RemovePaymentMethod(paymentMethodId);
            await _buyerRepository.UpdateAsync(buyer);

            _logger.LogInformation($"Deleted saved card {paymentMethodId} for shopper.");
            return true;
        }
    }

    private static string SafeLast4(string? number)
    {
        if (string.IsNullOrEmpty(number) || number.Length < 4) return string.Empty;
        return number.Substring(number.Length - 4);
    }

    private static string BuildVaultIdempotencyKey(string buyerId, string last4, string expiry)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{buyerId}|{last4}|{expiry}"));
        // PayPal-Request-Id max length 108; a hex-encoded prefix is well within bounds.
        var hex = Convert.ToHexString(hash).Substring(0, 32).ToLowerInvariant();
        return $"eshop-vault-{hex}";
    }
}
