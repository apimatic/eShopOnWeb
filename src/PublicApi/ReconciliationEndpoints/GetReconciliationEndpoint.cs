using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IPaymentReconciliationService service) =>
            {
                return await HandleAsync(new GetReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IPaymentReconciliationService service)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from))
        {
            throw new PaymentException("'from' must be an ISO-8601 date-time.");
        }

        if (!DateTimeOffset.TryParse(request.To, out var to))
        {
            throw new PaymentException("'to' must be an ISO-8601 date-time.");
        }

        var report = await service.ReconcileAsync(from, to);
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            EshopPaymentCount = report.EshopPaymentCount,
            MatchedCount = report.MatchedCount,
            PayPalOnlyCount = report.PayPalOnlyCount,
            EshopOnlyCount = report.EshopOnlyCount,
            Rows = report.Rows.Select(r => new ReconciliationRowResponse
            {
                OrderId = r.EshopOrderId,
                PayPalTransactionId = r.PayPalTransactionId,
                MatchStatus = r.MatchStatus,
                EshopPaymentState = r.EshopPaymentState,
                PayPalStatus = r.PayPalStatus,
                Amount = r.Amount,
                Currency = r.Currency,
                OccurredAt = r.OccurredAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class GetReconciliationRequest : BaseRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int EshopPaymentCount { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public List<ReconciliationRowResponse> Rows { get; set; } = new();
}

public class ReconciliationRowResponse
{
    public string? OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
    public string? EshopPaymentState { get; set; }
    public string? PayPalStatus { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? OccurredAt { get; set; }
}
