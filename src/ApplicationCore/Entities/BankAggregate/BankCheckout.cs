using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;

/// <summary>A pay-by-bank checkout: started, possibly re-priced, then confirmed once.</summary>
public class BankCheckout : IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private BankCheckout() { }
#pragma warning restore CS8618

    public BankCheckout(string userName, int connectionId, string itemsJson, decimal subtotal, decimal shippingCost,
        string street, string city, string state, string country, string zipCode)
    {
        Id = Guid.NewGuid();
        UserName = userName;
        ConnectionId = connectionId;
        ItemsJson = itemsJson;
        Subtotal = subtotal;
        ShippingCost = shippingCost;
        Street = street;
        City = city;
        State = state;
        Country = country;
        ZipCode = zipCode;
    }

    public Guid Id { get; private set; }
    public string UserName { get; private set; }
    public int ConnectionId { get; private set; }
    public string ItemsJson { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal ShippingCost { get; private set; }
    public string Street { get; private set; }
    public string City { get; private set; }
    public string State { get; private set; }
    public string Country { get; private set; }
    public string ZipCode { get; private set; }
    public DateTimeOffset CreatedOn { get; private set; } = DateTimeOffset.UtcNow;

    public decimal Total => Subtotal + ShippingCost;

    public int? OrderId { get; private set; }
    public string? ConfirmedSpeed { get; private set; }
    public bool IsConfirmed => OrderId.HasValue;

    public void ChangeShipping(decimal shippingCost) => ShippingCost = shippingCost;

    public void MarkConfirmed(int orderId, string speed)
    {
        OrderId = orderId;
        ConfirmedSpeed = speed;
    }
}
