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

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentReconciliationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                OrderId = m.OrderId,
                PayPalTransactionId = m.PayPalTransactionId,
                Status = m.Status,
                Amount = m.Amount
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(t => new PayPalTransactionDto
            {
                TransactionId = t.TransactionId,
                ReferenceId = t.ReferenceId,
                CustomField = t.CustomField,
                InvoiceId = t.InvoiceId,
                Status = t.Status,
                Amount = t.Amount?.Value,
                Currency = t.Amount?.Currency,
                Fee = t.Fee?.Value,
                InitiationDate = t.InitiationDate
            }).ToList(),
            LocalOnly = report.LocalOnly.Select(l => new LocalPaymentDto
            {
                OrderId = l.OrderId,
                Status = l.Status,
                PayPalOrderId = l.PayPalOrderId,
                AuthorizationId = l.AuthorizationId,
                CaptureId = l.CaptureId
            }).ToList()
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
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<PayPalTransactionDto> PayPalOnly { get; set; } = new();
    public List<LocalPaymentDto> LocalOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
}

public class PayPalTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? CustomField { get; set; }
    public string? InvoiceId { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class LocalPaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
}
