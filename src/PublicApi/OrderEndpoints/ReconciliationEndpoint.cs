using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationRequest : BaseRequest
{
    /// <summary>Start of the range (ISO-8601 date-time).</summary>
    [FromQuery(Name = "from")]
    [Required]
    public DateTimeOffset? From { get; set; }

    /// <summary>End of the range (ISO-8601 date-time).</summary>
    [FromQuery(Name = "to")]
    [Required]
    public DateTimeOffset? To { get; set; }
}

public class ReconciliationEntryDto
{
    public string? PayPalTransactionId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public DateTimeOffset? Time { get; set; }
    public string? InvoiceId { get; set; }

    /// <summary>The eShop order this transaction lines up with, if any.</summary>
    public int? OrderId { get; set; }

    /// <summary>True when no eShop order matches this PayPal transaction.</summary>
    public bool MissingFromEShop => OrderId is null;
}

public class UnmatchedOrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public string? Currency { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TotalPayPalTransactions { get; set; }
    public List<ReconciliationEntryDto> Transactions { get; set; } = new();

    /// <summary>eShop payments in the range that PayPal's report does not know about.</summary>
    public List<UnmatchedOrderDto> MissingFromPayPal { get; set; } = new();
}

/// <summary>
/// Operator action: lines PayPal's own transaction report for a date range up against
/// eShop orders, surfacing payments only one side knows about. Covers the whole range.
/// </summary>
public class ReconciliationEndpoint : EndpointBaseAsync
    .WithRequest<ReconciliationRequest>
    .WithActionResult<ReconciliationResponse>
{
    private const int PageSize = 500;

    private readonly IPaymentGateway _paymentGateway;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationEndpoint(IPaymentGateway paymentGateway, IReadRepository<Order> orderRepository)
    {
        _paymentGateway = paymentGateway;
        _orderRepository = orderRepository;
    }

    [HttpGet("api/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Reconciles PayPal transactions against eShop orders",
        Description = "Lists PayPal's own record of transactions for the range and matches them against eShop orders. Administrator role required.",
        OperationId = "reconciliation.list",
        Tags = new[] { "OrderEndpoints" })
    ]
    public override async Task<ActionResult<ReconciliationResponse>> HandleAsync(ReconciliationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.From is null || request.To is null)
        {
            return BadRequest("Both 'from' and 'to' query parameters (ISO-8601 date-times) are required.");
        }
        if (request.From >= request.To)
        {
            return BadRequest("'from' must be earlier than 'to'.");
        }

        var transactions = await GetAllTransactionsAsync(request.From.Value, request.To.Value, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpec(), cancellationToken);

        var response = new ReconciliationResponse
        {
            From = request.From.Value,
            To = request.To.Value,
            TotalPayPalTransactions = transactions.Count
        };

        var matchedOrderIds = new HashSet<int>();
        foreach (var txn in transactions)
        {
            var match = FindMatchingOrder(txn, orders);
            if (match is not null)
            {
                matchedOrderIds.Add(match.Id);
            }
            response.Transactions.Add(new ReconciliationEntryDto
            {
                PayPalTransactionId = txn.TransactionId,
                PayPalReferenceId = txn.ReferenceId,
                EventCode = txn.EventCode,
                Status = txn.Status,
                Amount = txn.Amount,
                Currency = txn.CurrencyCode,
                FeeAmount = txn.FeeAmount,
                Time = txn.InitiationTime,
                InvoiceId = txn.InvoiceId,
                OrderId = match?.Id
            });
        }

        // eShop payments in the range that the PayPal report does not mention.
        foreach (var order in orders)
        {
            if (matchedOrderIds.Contains(order.Id))
            {
                continue;
            }
            var payment = order.Payment!;
            var inRange = (payment.CreatedAt >= request.From && payment.CreatedAt <= request.To)
                          || (payment.UpdatedAt >= request.From && payment.UpdatedAt <= request.To)
                          || payment.Refunds.Any(r => r.CreatedAt >= request.From && r.CreatedAt <= request.To);
            if (!inRange)
            {
                continue;
            }
            response.MissingFromPayPal.Add(new UnmatchedOrderDto
            {
                OrderId = order.Id,
                BuyerId = order.BuyerId,
                Status = order.Status.ToString(),
                AuthorizationId = payment.AuthorizationId,
                CaptureId = payment.CaptureId,
                CapturedAmount = payment.CapturedAmount,
                Currency = payment.CurrencyCode
            });
        }

        return response;
    }

    private async Task<List<GatewayTransaction>> GetAllTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var all = new List<GatewayTransaction>();
        var page = 1;
        while (true)
        {
            var result = await _paymentGateway.GetTransactionsAsync(from, to, page, PageSize, cancellationToken);
            all.AddRange(result.Transactions);
            if (page >= Math.Max(result.TotalPages, 1) || result.Transactions.Count == 0)
            {
                break;
            }
            page++;
        }
        return all;
    }

    private static Order? FindMatchingOrder(GatewayTransaction txn, List<Order> orders)
    {
        return orders.FirstOrDefault(o =>
        {
            var payment = o.Payment!;
            if (txn.TransactionId is not null &&
                (txn.TransactionId == payment.AuthorizationId
                 || txn.TransactionId == payment.CaptureId
                 || txn.TransactionId == payment.PayPalOrderId
                 || payment.Refunds.Any(r => r.RefundId == txn.TransactionId)))
            {
                return true;
            }
            if (txn.ReferenceId is not null && txn.ReferenceId == payment.PayPalOrderId)
            {
                return true;
            }
            return (txn.InvoiceId is not null && txn.InvoiceId == payment.InvoiceId)
                   || (txn.CustomField is not null && txn.CustomField == payment.InvoiceId);
        });
    }
}
