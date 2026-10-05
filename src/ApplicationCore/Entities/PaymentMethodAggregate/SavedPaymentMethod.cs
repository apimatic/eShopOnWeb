using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

public enum SavedPaymentMethodStatus
{
    /// <summary>The vault call is in flight (or its outcome is unknown — see <see cref="SavedPaymentMethod.OutcomeUnknownSince"/>).</summary>
    Saving = 0,
    Active = 1,
    Failed = 2,
    Deleted = 3
}

/// <summary>
/// A card the shopper saved. Only PayPal's vault token and display details are kept here — never the
/// card number or security code.
/// </summary>
public class SavedPaymentMethod : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SavedPaymentMethod() { }

    public SavedPaymentMethod(string buyerId, string payPalRequestId, string? payPalCustomerId, DateTimeOffset now)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(payPalRequestId, nameof(payPalRequestId));
        BuyerId = buyerId;
        PayPalRequestId = payPalRequestId;
        PayPalCustomerId = payPalCustomerId;
        Status = SavedPaymentMethodStatus.Saving;
        CreatedAt = now;
    }

    public string BuyerId { get; private set; }
    public SavedPaymentMethodStatus Status { get; private set; }
    public string PayPalRequestId { get; private set; }
    public string? PayPalVaultId { get; private set; }
    public string? PayPalCustomerId { get; private set; }
    public string? Brand { get; private set; }
    public string? LastDigits { get; private set; }
    public string? Expiry { get; private set; }
    public string? CardholderName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? OutcomeUnknownSince { get; private set; }

    /// <summary>True once PayPal confirmed the vault token is gone (or it never existed).</summary>
    public bool VaultTokenRemoved { get; private set; }

    public bool IsUsable => Status == SavedPaymentMethodStatus.Active && !string.IsNullOrEmpty(PayPalVaultId);

    public void Vaulted(string vaultId, string? customerId, string? brand, string? lastDigits, string? expiry, string? cardholderName)
    {
        PayPalVaultId = vaultId;
        PayPalCustomerId = customerId ?? PayPalCustomerId;
        Brand = brand;
        LastDigits = lastDigits;
        Expiry = expiry;
        CardholderName = cardholderName;
        Status = SavedPaymentMethodStatus.Active;
        OutcomeUnknownSince = null;
    }

    public void SaveFailed()
    {
        Status = SavedPaymentMethodStatus.Failed;
        VaultTokenRemoved = true;
        OutcomeUnknownSince = null;
    }

    public void SaveOutcomeUnknown(DateTimeOffset now) => OutcomeUnknownSince ??= now;

    /// <summary>Removes the card from the shopper's view and from use immediately, before PayPal is told.</summary>
    public void Delete(DateTimeOffset now)
    {
        Status = SavedPaymentMethodStatus.Deleted;
        DeletedAt ??= now;
    }

    public void VaultTokenDeleted() => VaultTokenRemoved = true;
}
