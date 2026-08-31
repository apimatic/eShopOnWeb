using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: PayPal's own record of transactions over [from, to]
/// (ISO-8601 date-times) lined up against eShop orders, in both directions.
/// Covers the whole range, following PayPal's pagination.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IReconciliationService reconciliationService, CancellationToken ct) =>
            {
                if (from is null || to is null)
                {
                    return Results.BadRequest(new { message = "Both 'from' and 'to' query parameters are required (ISO-8601 date-times)." });
                }

                var report = await reconciliationService.BuildReportAsync(from.Value, to.Value, ct);

                var response = new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    Currency = report.Currency,
                    PayPalTransactions = report.PayPalTransactions,
                    LocalPaymentsNotInPayPalReport = report.LocalPaymentsNotInPayPalReport
                };
                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string Currency { get; set; } = string.Empty;
    public IReadOnlyList<ReconciliationLine> PayPalTransactions { get; set; } = Array.Empty<ReconciliationLine>();
    public IReadOnlyList<ReconciliationUnmatchedLocal> LocalPaymentsNotInPayPalReport { get; set; } =
        Array.Empty<ReconciliationUnmatchedLocal>();
}
