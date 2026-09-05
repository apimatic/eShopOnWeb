using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

/// <summary>
/// A card the shopper has saved so a later order can be paid for without re-entering it.
/// Only the processor's token and what is safe to show the shopper are stored here; a card number
/// never reaches this application's database or its logs.
/// </summary>
public class PaymentMethod : BaseEntity, IAggregateRoot
{
    /// <summary>The shopper who saved the card. Nothing is visible or usable without matching it.</summary>
    public string BuyerId { get; private set; }

    /// <summary>
    /// The processor's vault (payment method token) id for the card. This is a reference into a PCI
    /// compliant system, never card data itself.
    /// </summary>
    public string? CardId { get; private set; }

    /// <summary>PayPal's customer id that the vault id belongs to. Required to use or delete the token.</summary>
    public string? PayPalCustomerId { get; private set; }

    public string? Alias { get; private set; }
    public string? Last4 { get; private set; }
    public string? Brand { get; private set; }

    /// <summary>Card expiry in PayPal's YYYY-MM form.</summary>
    public string? Expiry { get; private set; }

    public string? CardHolderName { get; private set; }
    public string? BillingCountry { get; private set; }
    public DateTimeOffset Created { get; private set; } = DateTimeOffset.Now;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentMethod() { }
#pragma warning restore CS8618 // Required by Entity Framework

    public PaymentMethod(string buyerId, string cardId, string? payPalCustomerId, string? alias,
        string? last4, string? brand, string? expiry, string? cardHolderName, string? billingCountry)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(cardId, nameof(cardId));

        BuyerId = buyerId;
        CardId = cardId;
        PayPalCustomerId = payPalCustomerId;
        Alias = alias;
        Last4 = last4;
        Brand = brand;
        Expiry = expiry;
        CardHolderName = cardHolderName;
        BillingCountry = billingCountry;
    }

    /// <summary>How the shopper is shown which card this is, e.g. "VISA ending 1111 (exp 2030-11)".</summary>
    public string Description
    {
        get
        {
            var brand = string.IsNullOrWhiteSpace(Brand) ? "card" : Brand;
            var last4 = string.IsNullOrWhiteSpace(Last4) ? "----" : Last4;
            var expiry = string.IsNullOrWhiteSpace(Expiry) ? "n/a" : Expiry;
            var suffix = string.IsNullOrWhiteSpace(Alias) ? string.Empty : $" \"{Alias}\"";
            return $"{brand} ending {last4} (expires {expiry}){suffix}";
        }
    }
}
