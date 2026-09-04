using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationRowDto> Transactions { get; set; } = new();
    public List<ReconciliationOrderDto> OrdersWithoutPayPalTransaction { get; set; } = new();
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
}

public class ReconciliationRowDto
{
    public string TransactionId { get; set; } = "";
    public string TransactionType { get; set; } = "";
    public string TransactionStatus { get; set; } = "";
    public DateTimeOffset TransactionDate { get; set; }
    public string? Amount { get; set; }
    public string? Fee { get; set; }
    public string? Net { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public int? OrderId { get; set; }
    public string? OrderStatus { get; set; }
    public string MatchState { get; set; } = "";
}

public class ReconciliationOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "";
    public decimal Total { get; set; }
    public string Currency { get; set; } = "";
    public DateTimeOffset OrderDate { get; set; }
    public string? PaymentStatus { get; set; }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
}

/// <summary>
/// Lines up PayPal's own transaction record for a date range against eShop orders,
/// so a payment PayPal knows about and eShop doesn't - or the reverse - is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IPaymentService paymentService) =>
                await HandleAsync(from, to, paymentService))
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request) => throw new NotSupportedException();

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IPaymentService paymentService)
    {
        if (from == default || to == default)
        {
            return Results.BadRequest(new { error = "Both 'from' and 'to' ISO-8601 date-times are required." });
        }

        var report = await paymentService.ReconcileAsync(from, to);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To
        };

        foreach (var row in report.Rows)
        {
            response.Transactions.Add(new ReconciliationRowDto
            {
                TransactionId = row.TransactionId,
                TransactionType = row.TransactionType,
                TransactionStatus = row.TransactionStatus,
                TransactionDate = row.TransactionDate,
                Amount = row.Amount == null ? null : $"{row.Amount.Formatted} {row.Amount.CurrencyCode}",
                Fee = row.Fee == null ? null : $"{row.Fee.Formatted} {row.Fee.CurrencyCode}",
                Net = row.Net == null ? null : $"{row.Net.Formatted} {row.Net.CurrencyCode}",
                InvoiceId = row.InvoiceId,
                CustomId = row.CustomId,
                OrderId = row.OrderId,
                OrderStatus = row.OrderStatus,
                MatchState = row.MatchState.ToString()
            });
            switch (row.MatchState)
            {
                case ReconciliationMatchState.Matched: response.MatchedCount++; break;
                case ReconciliationMatchState.PayPalOnly: response.PayPalOnlyCount++; break;
                case ReconciliationMatchState.EshopOnly: response.EshopOnlyCount++; break;
            }
        }

        foreach (var order in report.OrdersWithoutPayPalTransaction)
        {
            response.OrdersWithoutPayPalTransaction.Add(new ReconciliationOrderDto
            {
                OrderId = order.OrderId,
                Status = order.Status,
                Total = order.Total,
                Currency = order.Currency,
                OrderDate = order.OrderDate,
                PaymentStatus = order.PaymentStatus
            });
        }
        response.EshopOnlyCount += response.OrdersWithoutPayPalTransaction.Count;

        return Results.Ok(response);
    }
}
