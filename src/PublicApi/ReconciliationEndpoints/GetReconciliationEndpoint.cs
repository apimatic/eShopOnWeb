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

public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IPaymentReconciliationService service) =>
            {
                return await HandleAsync(new GetReconciliationRequest(from, to), service);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IPaymentReconciliationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        return Results.Ok(GetReconciliationResponse.Create(report));
    }
}

public class GetReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }

    public GetReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class GetReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<PayPalTransactionDto> PayPalTransactions { get; set; } = new();
    public List<MatchedTransactionDto> Matched { get; set; } = new();
    public List<PayPalTransactionDto> PayPalOnly { get; set; } = new();
    public List<EshopOnlyDto> EshopOnly { get; set; } = new();

    public static GetReconciliationResponse Create(ReconciliationReport report)
    {
        return new GetReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactions = report.PayPalTransactions.Select(PayPalTransactionDto.From).ToList(),
            Matched = report.Matched.Select(m => new MatchedTransactionDto
            {
                OrderId = m.OrderId,
                PayPalTransaction = PayPalTransactionDto.From(m.PayPalTransaction)
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(PayPalTransactionDto.From).ToList(),
            EshopOnly = report.EshopOnly.Select(e => new EshopOnlyDto
            {
                OrderId = e.OrderId,
                Status = e.Status,
                PayPalOrderId = e.PayPalOrderId,
                PayPalAuthorizationId = e.PayPalAuthorizationId,
                PayPalCaptureId = e.PayPalCaptureId
            }).ToList()
        };
    }
}

public class PayPalTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Fee { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }

    public static PayPalTransactionDto From(PayPalReportedTransaction transaction)
    {
        return new PayPalTransactionDto
        {
            TransactionId = transaction.TransactionId,
            ReferenceId = transaction.ReferenceId,
            InvoiceId = transaction.InvoiceId,
            CustomField = transaction.CustomField,
            EventCode = transaction.EventCode,
            Status = transaction.Status,
            Amount = transaction.AmountValue,
            Currency = transaction.CurrencyCode,
            Fee = transaction.FeeValue,
            InitiationDate = transaction.InitiationDate
        };
    }
}

public class MatchedTransactionDto
{
    public int OrderId { get; set; }
    public PayPalTransactionDto PayPalTransaction { get; set; } = new();
}

public class EshopOnlyDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? PayPalAuthorizationId { get; set; }
    public string? PayPalCaptureId { get; set; }
}
