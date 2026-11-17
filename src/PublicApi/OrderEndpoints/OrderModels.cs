using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Finix;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class AddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }

    public Address ToAddress()
    {
        return new Address(
            string.IsNullOrWhiteSpace(Street) ? "123 Main St" : Street,
            string.IsNullOrWhiteSpace(City) ? "San Francisco" : City,
            string.IsNullOrWhiteSpace(State) ? "CA" : State,
            string.IsNullOrWhiteSpace(Country) ? "USA" : Country,
            string.IsNullOrWhiteSpace(ZipCode) ? "94105" : ZipCode);
    }
}

public class CardAddressRequest
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }

    public CardAddress ToCardAddress()
    {
        return new CardAddress(
            string.IsNullOrWhiteSpace(Line1) ? "123 Main St" : Line1,
            Line2,
            string.IsNullOrWhiteSpace(City) ? "San Francisco" : City,
            string.IsNullOrWhiteSpace(Region) ? "CA" : Region,
            string.IsNullOrWhiteSpace(PostalCode) ? "94105" : PostalCode,
            string.IsNullOrWhiteSpace(Country) ? "USA" : Country);
    }
}

public class CardRequest
{
    public string? Number { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardAddressRequest? Address { get; set; }

    public CardDetails ToCardDetails()
    {
        return new CardDetails(
            Number ?? string.Empty,
            ExpirationMonth,
            ExpirationYear,
            SecurityCode ?? string.Empty,
            Name ?? string.Empty,
            Address?.ToCardAddress());
    }
}

public class RefundDto
{
    public int RefundId { get; set; }
    public decimal Amount { get; set; }
    public string State { get; set; } = string.Empty;
    public string FinixTransferId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundDto From(RefundView refund)
    {
        return new RefundDto
        {
            RefundId = refund.RefundId,
            Amount = refund.Amount,
            State = refund.State,
            FinixTransferId = refund.FinixTransferId,
            CreatedAt = refund.CreatedAt
        };
    }
}

public class OrderPaymentDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string FulfilmentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureTransferState { get; set; }
    public decimal? AuthorizedAmount { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? Fee { get; set; }
    public decimal? Net { get; set; }
    public decimal? RefundedAmount { get; set; }
    public string? FailureMessage { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();

    public static OrderPaymentDto From(PaymentView payment)
    {
        return new OrderPaymentDto
        {
            OrderId = payment.OrderId,
            OrderDate = payment.OrderDate,
            Total = payment.Total,
            Currency = payment.Currency,
            PaymentStatus = payment.PaymentStatus,
            FulfilmentStatus = payment.FulfilmentStatus,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationState = payment.AuthorizationState,
            CaptureTransferId = payment.CaptureTransferId,
            CaptureTransferState = payment.CaptureTransferState,
            AuthorizedAmount = payment.AuthorizedAmount,
            CapturedAmount = payment.CapturedAmount,
            Fee = payment.Fee,
            Net = payment.Net,
            RefundedAmount = payment.RefundedAmount,
            FailureMessage = payment.FailureMessage,
            Refunds = payment.Refunds.Select(RefundDto.From).ToList()
        };
    }
}

public class ReconciliationFinixTransactionDto
{
    public string Kind { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? ParentId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? MatchedOrderId { get; set; }
}

public class ReconciliationOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string FulfilmentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public List<string> RefundTransferIds { get; set; } = new();
}
