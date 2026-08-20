using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new GetReconciliationRequest { From = from, To = to }, paymentService);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IOrderPaymentService paymentService)
    {
        var report = await paymentService.ReconcileAsync(request.From, request.To);
        var response = new GetReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Matches = report.Matches.Select(match => new ReconciliationMatchDto
            {
                Order = PaymentDtoFactory.From(match.Order.Order, match.Order.Payment),
                PayPalTransaction = ToDto(match.Transaction)
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(ToDto).ToList(),
            EshopOnly = report.EshopOnly.Select(item => PaymentDtoFactory.From(item.Order, item.Payment)).ToList()
        };
        return Results.Ok(response);
    }

    private static PayPalTransactionDto ToDto(ApplicationCore.PayPal.PayPalReportedTransaction transaction)
    {
        return new PayPalTransactionDto
        {
            TransactionId = transaction.TransactionId,
            PaypalReferenceId = transaction.PaypalReferenceId,
            InvoiceId = transaction.InvoiceId,
            CustomField = transaction.CustomField,
            EventCode = transaction.EventCode,
            Status = transaction.Status,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            InitiationDate = transaction.InitiationDate,
            FeeAmount = transaction.FeeAmount
        };
    }
}

public class GetReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public GetReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatchDto> Matches { get; set; } = new();
    public List<PayPalTransactionDto> PayPalOnly { get; set; } = new();
    public List<OrderDto> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public OrderDto Order { get; set; } = new();
    public PayPalTransactionDto PayPalTransaction { get; set; } = new();
}

public class PayPalTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? PaypalReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public decimal? FeeAmount { get; set; }
}
