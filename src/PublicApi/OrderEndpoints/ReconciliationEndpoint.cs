using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and
/// lines them up against eShop orders. Covers the whole range, not just one page.
/// Note: PayPal reporting lags live activity (up to ~3 hours), so very recent
/// payments may legitimately be absent from PayPal's side of the report.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly IReconciliationService _reconciliationService;

    public ReconciliationEndpoint(IReconciliationService reconciliationService)
    {
        _reconciliationService = reconciliationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to) =>
            {
                if (from is null || to is null)
                {
                    throw new PaymentDomainException("Both 'from' and 'to' query parameters (ISO-8601 date-times) are required.");
                }
                return await HandleAsync(from.Value, to.Value);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var report = await _reconciliationService.GetReportAsync(from, to);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            Transactions = new List<ReconciliationEntry>(report.Transactions),
            PaymentsMissingFromPayPal = new List<UnmatchedPayment>(report.PaymentsMissingFromPayPal)
        };
        return Results.Ok(response);
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public List<ReconciliationEntry> Transactions { get; set; } = new();
    public List<UnmatchedPayment> PaymentsMissingFromPayPal { get; set; } = new();
}
