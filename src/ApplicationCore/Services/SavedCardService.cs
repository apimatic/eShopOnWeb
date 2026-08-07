using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SavedCardService : ISavedCardService
{
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalPaymentGateway _paymentGateway;
    private readonly IAppLogger<SavedCardService> _logger;

    public SavedCardService(
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalPaymentGateway paymentGateway,
        IAppLogger<SavedCardService> logger)
    {
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? label,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Fresh idempotency key: saving is a create, so retries within one submission are de-duped by
        // PayPal while distinct saves of the same card remain allowed.
        var idempotencyKey = $"vault-{Guid.NewGuid():N}";

        var vaulted = await _paymentGateway.VaultCardAsync(card, idempotencyKey, cancellationToken);

        // Only the safe descriptor (token id + brand + last4 + expiry) is stored — never the PAN/CVV.
        var savedCard = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.Last4,
            vaulted.Expiry, label, DateTimeOffset.UtcNow);
        savedCard = await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation("Saved card {0} ({1} ****{2}) for buyer.", savedCard.Id, savedCard.CardBrand, savedCard.Last4);
        return savedCard;
    }
}
