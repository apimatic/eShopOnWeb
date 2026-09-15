using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationQuery(from, to), service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IPaymentReconciliationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(row => new ReconciliationMatchDto
            {
                Order = OrderApiMapper.ToResponse(row.Order),
                PaypalTransaction = ToDto(row.PaypalTransaction)
            }).ToList(),
            PaypalOnly = report.PaypalOnly.Select(ToDto).ToList(),
            EshopOnly = report.EshopOnly.Select(OrderApiMapper.ToResponse).ToList()
        });
    }

    private static PaypalTransactionDto ToDto(PayPalReportedTransaction transaction)
    {
        return new PaypalTransactionDto
        {
            TransactionId = transaction.TransactionId,
            ReferenceId = transaction.ReferenceId,
            CustomField = transaction.CustomField,
            InvoiceId = transaction.InvoiceId,
            Status = transaction.Status,
            EventCode = transaction.EventCode,
            InitiationDate = transaction.InitiationDate,
            Amount = transaction.Amount,
            FeeAmount = transaction.FeeAmount,
            Currency = transaction.Currency
        };
    }
}

public record ReconciliationQuery(DateTimeOffset From, DateTimeOffset To);

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationMatchDto> Matched { get; set; } = new();
    public System.Collections.Generic.List<PaypalTransactionDto> PaypalOnly { get; set; } = new();
    public System.Collections.Generic.List<OrderResponse> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public OrderResponse Order { get; set; } = new();
    public PaypalTransactionDto PaypalTransaction { get; set; } = new();
}

public class PaypalTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? CustomField { get; set; }
    public string? InvoiceId { get; set; }
    public string? Status { get; set; }
    public string? EventCode { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public decimal? Amount { get; set; }
    public decimal? FeeAmount { get; set; }
    public string? Currency { get; set; }
}
