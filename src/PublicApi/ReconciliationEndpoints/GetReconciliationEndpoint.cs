using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range
/// (paging and 31-day windowing handled internally) and lines them up against
/// eShop orders, so discrepancies in either direction are visible.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly IPayPalGateway _payPalGateway;
    private readonly IRepository<OrderPayment> _paymentRepository;

    public GetReconciliationEndpoint(IPayPalGateway payPalGateway,
        IRepository<OrderPayment> paymentRepository)
    {
        _payPalGateway = payPalGateway;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (request.To <= request.From)
        {
            throw new PaymentConflictException("The 'to' date-time must be after the 'from' date-time.");
        }

        var transactions = await _payPalGateway.ListTransactionsAsync(request.From, request.To);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsWithCapturesSpec());

        var lines = new List<ReconciliationLineDto>();
        var matchedPaymentIds = new HashSet<int>();

        foreach (var transaction in transactions)
        {
            var match = payments.FirstOrDefault(p =>
                p.CaptureId == transaction.TransactionId ||
                p.AuthorizationId == transaction.TransactionId ||
                p.PayPalOrderId == transaction.TransactionId ||
                p.Refunds.Any(r => r.PayPalRefundId == transaction.TransactionId) ||
                p.CaptureId == transaction.ReferenceId ||
                p.AuthorizationId == transaction.ReferenceId ||
                p.PayPalOrderId == transaction.ReferenceId);

            if (match != null)
            {
                matchedPaymentIds.Add(match.Id);
            }

            lines.Add(new ReconciliationLineDto
            {
                TransactionId = transaction.TransactionId,
                ReferenceId = transaction.ReferenceId,
                EventCode = transaction.EventCode,
                Status = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Fee = transaction.Fee,
                TransactionDate = transaction.InitiationDate,
                InvoiceId = transaction.InvoiceId,
                OrderId = match?.OrderId,
                Match = match != null ? "Matched" : "MissingFromEShop"
            });
        }

        // Local captures that PayPal did not report inside the range.
        foreach (var payment in payments.Where(p => !matchedPaymentIds.Contains(p.Id)))
        {
            var relevant = (payment.CapturedAt ?? DateTimeOffset.MinValue) >= request.From
                && (payment.CapturedAt ?? DateTimeOffset.MinValue) <= request.To;
            if (!relevant)
            {
                continue;
            }

            lines.Add(new ReconciliationLineDto
            {
                TransactionId = payment.CaptureId,
                Status = payment.CaptureStatus,
                Amount = payment.CapturedAmount ?? payment.Amount,
                Currency = payment.Currency,
                TransactionDate = payment.CapturedAt,
                InvoiceId = $"ESHOP-ORDER-{payment.OrderId}",
                OrderId = payment.OrderId,
                Match = "MissingFromPayPal"
            });
        }

        var response = new ReconciliationResponse
        {
            From = request.From,
            To = request.To,
            Transactions = lines.OrderBy(l => l.TransactionDate).ToList()
        };
        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationLineDto> Transactions { get; set; } = new List<ReconciliationLineDto>();
}

public class ReconciliationLineDto
{
    public string? TransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? Fee { get; set; }
    public DateTimeOffset? TransactionDate { get; set; }
    public string? InvoiceId { get; set; }
    public int? OrderId { get; set; }

    /// <summary>Matched | MissingFromEShop | MissingFromPayPal</summary>
    public string Match { get; set; } = string.Empty;
}
