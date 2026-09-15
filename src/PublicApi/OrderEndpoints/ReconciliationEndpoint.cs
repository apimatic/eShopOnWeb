using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string? from, string? to, IOrderCheckoutService checkout) =>
            {
                if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromDate)
                    || !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toDate))
                {
                    throw new PaymentException(400, "`from` and `to` must be ISO-8601 date-times.", "INVALID_DATE_RANGE");
                }

                var report = await checkout.ReconcileAsync(fromDate, toDate);
                return Results.Ok(new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    Matched = report.Matched.Select(m => new ReconciliationMatchResponse
                    {
                        Order = OrderResponse.From(m.Order, m.Order.Currency ?? string.Empty),
                        Paypal = MapTxn(m.Transaction)
                    }).ToList(),
                    PaypalOnly = report.PaypalOnly.Select(MapTxn).ToList(),
                    EshopOnly = report.EshopOnly.Select(o => OrderResponse.From(o, o.Currency ?? string.Empty)).ToList()
                });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderCheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());

    private static PaypalTransactionResponse MapTxn(ApplicationCore.Payments.ReportedTransaction txn) => new()
    {
        TransactionId = txn.TransactionId,
        ReferenceId = txn.ReferenceId,
        ReferenceIdType = txn.ReferenceIdType,
        InvoiceId = txn.InvoiceId,
        CustomField = txn.CustomField,
        EventCode = txn.EventCode,
        Status = txn.Status,
        InitiationDate = txn.InitiationDate,
        Amount = txn.Amount,
        Currency = txn.Currency,
        FeeAmount = txn.FeeAmount
    };
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatchResponse> Matched { get; set; } = new();
    public List<PaypalTransactionResponse> PaypalOnly { get; set; } = new();
    public List<OrderResponse> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchResponse
{
    public OrderResponse Order { get; set; } = new();
    public PaypalTransactionResponse Paypal { get; set; } = new();
}

public class PaypalTransactionResponse
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
}
