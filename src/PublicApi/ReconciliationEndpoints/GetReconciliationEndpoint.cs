using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: PayPal's own record of transactions over a date range, lined
/// up against eShop payments. Covers the whole range (all pages).
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, IReconciliationService reconciliationService) =>
            {
                return await HandleAsync(new GetReconciliationRequest { From = from, To = to }, reconciliationService);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IReconciliationService reconciliationService)
    {
        if (request.From == default || request.To == default)
        {
            return Results.BadRequest(new { message = "Both 'from' and 'to' (ISO-8601 date-times) are required." });
        }

        var report = await reconciliationService.GetReconciliationAsync(request.From, request.To);

        var response = new GetReconciliationResponse
        {
            From = report.From,
            To = report.To
        };
        foreach (var entry in report.Entries)
        {
            response.Entries.Add(new ReconciliationEntryDto
            {
                PayPalTransactionId = entry.PayPalTransactionId,
                EventCode = entry.EventCode,
                PayPalStatus = entry.PayPalStatus,
                Amount = entry.Amount,
                Currency = entry.Currency,
                FeeAmount = entry.FeeAmount,
                TransactionDate = entry.TransactionDate,
                OrderId = entry.OrderId,
                PaymentId = entry.PaymentId,
                MatchStatus = entry.MatchStatus
            });
        }

        return Results.Ok(response);
    }
}

public class GetReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class GetReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new List<ReconciliationEntryDto>();
}

public class ReconciliationEntryDto
{
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public DateTimeOffset? TransactionDate { get; set; }
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
}
