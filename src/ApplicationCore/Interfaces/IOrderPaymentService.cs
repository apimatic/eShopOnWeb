using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> PayAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken);
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<OrderRefund> RefundAsync(string buyerId, int orderId, RefundOrderCommand command, CancellationToken cancellationToken);
}

public sealed class PayOrderCommand
{
    public string? PaymentMethodId { get; init; }
    public CardPaymentInput? Card { get; init; }
}

public sealed class RefundOrderCommand
{
    public required string IdempotencyKey { get; init; }
    public decimal? Amount { get; init; }
}

public sealed class CardPaymentInput
{
    public required string Name { get; init; }
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public required CardBillingAddressInput BillingAddress { get; init; }
}

public sealed class CardBillingAddressInput
{
    public required string CountryCode { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
}
