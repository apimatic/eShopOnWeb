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
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationEntryDto
{
    public string Outcome { get; set; } = string.Empty;
    public string? PayPalTransactionId { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public int? OrderId { get; set; }
    public string? EShopReference { get; set; }
    public string? EShopPaymentStatus { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int InPayPalOnlyCount { get; set; }
    public int InEShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// Operator report: lists PayPal's own record of transactions for a date range and lines them up
/// against eShop orders, covering the whole range (all pages). Restricted to administrators.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        try
        {
            var report = await service.ReconcileAsync(request.From, request.To);

            var response = new ReconciliationResponse(request.CorrelationId())
            {
                From = report.From,
                To = report.To,
                MatchedCount = report.MatchedCount,
                InPayPalOnlyCount = report.InPayPalOnlyCount,
                InEShopOnlyCount = report.InEShopOnlyCount,
                Entries = report.Entries.Select(e => new ReconciliationEntryDto
                {
                    Outcome = e.Outcome.ToString(),
                    PayPalTransactionId = e.PayPalTransactionId,
                    PayPalStatus = e.PayPalStatus,
                    PayPalAmount = e.PayPalAmount,
                    CurrencyCode = e.CurrencyCode,
                    OrderId = e.OrderId,
                    EShopReference = e.EShopReference,
                    EShopPaymentStatus = e.EShopPaymentStatus
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return PaymentProblem.ToResult(ex);
        }
    }
}
