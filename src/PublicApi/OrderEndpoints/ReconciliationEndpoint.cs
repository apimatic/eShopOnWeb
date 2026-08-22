using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string? LastRefreshedDatetime { get; set; }
    public System.Collections.Generic.List<PayPalTransactionResponse> PayPalTransactions { get; set; } = new();
    public System.Collections.Generic.List<OrderResponse> EShopOrders { get; set; } = new();
    public System.Collections.Generic.List<PayPalTransactionResponse> UnmatchedPayPal { get; set; } = new();
    public System.Collections.Generic.List<OrderResponse> UnmatchedEShop { get; set; } = new();
}

public class PayPalTransactionResponse
{
    public string TransactionId { get; set; } = string.Empty;
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? Status { get; set; }
    public string? AmountValue { get; set; }
    public string? FeeAmountValue { get; set; }
    public string? Currency { get; set; }
    public string? InitiationDate { get; set; }
    public string? PaypalReferenceId { get; set; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, ICheckoutPaymentService checkout) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, checkout);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, ICheckoutPaymentService checkout)
    {
        var report = await checkout.ReconcileAsync(request.From, request.To, default);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            LastRefreshedDatetime = report.LastRefreshedDatetime,
            PayPalTransactions = report.PayPalTransactions.Select(MapTxn).ToList(),
            EShopOrders = report.EShopOrdersInRange.Select(OrderResponseMapper.Map).ToList(),
            UnmatchedPayPal = report.UnmatchedPayPal.Select(MapTxn).ToList(),
            UnmatchedEShop = report.UnmatchedEShop.Select(OrderResponseMapper.Map).ToList()
        });
    }

    private static PayPalTransactionResponse MapTxn(ApplicationCore.Payment.PayPalReportedTransaction txn) =>
        new()
        {
            TransactionId = txn.TransactionId,
            InvoiceId = txn.InvoiceId,
            CustomField = txn.CustomField,
            Status = txn.Status,
            AmountValue = txn.AmountValue,
            FeeAmountValue = txn.FeeAmountValue,
            Currency = txn.Currency,
            InitiationDate = txn.InitiationDate,
            PaypalReferenceId = txn.PaypalReferenceId
        };
}
