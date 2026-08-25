using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TotalPayPalTransactions { get; set; }
    public int TotalEShopPayments { get; set; }
    public List<MatchedRecord> Matched { get; set; } = new();
    public List<PayPalOnlyRecord> PayPalOnly { get; set; } = new();
    public List<EShopOnlyRecord> EShopOnly { get; set; } = new();
}

public class MatchedRecord
{
    public string PayPalTransactionId { get; set; } = "";
    public int EShopOrderId { get; set; }
    public string EShopPaymentStatus { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public DateTimeOffset? TransactionDate { get; set; }
}

public class PayPalOnlyRecord
{
    public string TransactionId { get; set; } = "";
    public string EventCode { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset? TransactionDate { get; set; }
    public string PayerEmail { get; set; } = "";
}

public class EShopOnlyRecord
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = "";
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, object, IRepository<OrderPayment>>
{
    private readonly IPayPalClient _paypal;

    public ReconciliationEndpoint(IPayPalClient paypal)
    {
        _paypal = paypal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
                       Roles = "Administrators")]
            async (DateTimeOffset from, DateTimeOffset to, IRepository<OrderPayment> paymentRepo,
                   CancellationToken ct) =>
            {
                return await BuildReport(from, to, paymentRepo, ct);
            })
            .Produces<ReconciliationResponse>()
            .Produces(400)
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(object request, IRepository<OrderPayment> repository)
        => Task.FromResult(Results.Ok() as IResult);

    private async Task<IResult> BuildReport(DateTimeOffset from, DateTimeOffset to,
        IRepository<OrderPayment> paymentRepo, CancellationToken ct)
    {
        if (from >= to)
            return Results.BadRequest(new { error = "'from' must be before 'to'." });

        // PayPal transaction search is limited to 31-day windows
        if ((to - from).TotalDays > 31)
            return Results.BadRequest(new { error = "Date range must not exceed 31 days." });

        List<PayPalTransactionRecord> paypalTxns;
        try
        {
            paypalTxns = await _paypal.SearchTransactionsAsync(from, to, ct);
        }
        catch (PayPalException ex)
        {
            return Results.BadRequest(new { error = $"PayPal search failed: {ex.Message}", code = ex.PayPalErrorName });
        }

        var eShopPaymentsSpec = new AllOrderPaymentsSpec();
        var eShopPayments = await paymentRepo.ListAsync(eShopPaymentsSpec, ct);

        // Build lookup maps: PayPal transaction ID → eShop payment
        // A PayPal transaction ID can be a capture ID or an auth ID
        var captureIdMap = eShopPayments
            .Where(p => !string.IsNullOrEmpty(p.CaptureId))
            .ToDictionary(p => p.CaptureId!, p => p);

        var authIdMap = eShopPayments
            .Where(p => !string.IsNullOrEmpty(p.AuthorizationId))
            .ToDictionary(p => p.AuthorizationId!, p => p);

        var matched = new List<MatchedRecord>();
        var paypalOnly = new List<PayPalOnlyRecord>();
        var matchedEShopIds = new HashSet<int>();

        foreach (var txn in paypalTxns)
        {
            OrderPayment? eShopPayment = null;
            captureIdMap.TryGetValue(txn.TransactionId, out eShopPayment);
            if (eShopPayment == null)
                authIdMap.TryGetValue(txn.TransactionId, out eShopPayment);

            if (eShopPayment != null)
            {
                matchedEShopIds.Add(eShopPayment.Id);
                matched.Add(new MatchedRecord
                {
                    PayPalTransactionId = txn.TransactionId,
                    EShopOrderId = eShopPayment.OrderId,
                    EShopPaymentStatus = eShopPayment.Status.ToString(),
                    Amount = txn.Amount,
                    Currency = txn.Currency,
                    TransactionDate = txn.InitiationDate
                });
            }
            else
            {
                paypalOnly.Add(new PayPalOnlyRecord
                {
                    TransactionId = txn.TransactionId,
                    EventCode = txn.EventCode,
                    Amount = txn.Amount,
                    Currency = txn.Currency,
                    Status = txn.Status,
                    TransactionDate = txn.InitiationDate,
                    PayerEmail = txn.PayerEmail
                });
            }
        }

        // eShop payments with a PayPal ID that didn't show up in PayPal's report
        // (includes payments in Authorized/Captured state not yet visible due to reporting lag)
        var eShopOnly = eShopPayments
            .Where(p => p.Status is OrderPaymentStatus.Authorized
                        or OrderPaymentStatus.Captured
                        or OrderPaymentStatus.PartiallyRefunded
                        or OrderPaymentStatus.FullyRefunded)
            .Where(p => !matchedEShopIds.Contains(p.Id))
            .Where(p => p.CreatedAt >= from && p.CreatedAt <= to)
            .Select(p => new EShopOnlyRecord
            {
                OrderId = p.OrderId,
                PaymentStatus = p.Status.ToString(),
                AuthorizationId = p.AuthorizationId,
                CaptureId = p.CaptureId,
                Amount = p.Amount,
                Currency = p.Currency,
                CreatedAt = p.CreatedAt
            })
            .ToList();

        return Results.Ok(new ReconciliationResponse
        {
            From = from,
            To = to,
            TotalPayPalTransactions = paypalTxns.Count,
            TotalEShopPayments = eShopPayments.Count,
            Matched = matched,
            PayPalOnly = paypalOnly,
            EShopOnly = eShopOnly
        });
    }
}
