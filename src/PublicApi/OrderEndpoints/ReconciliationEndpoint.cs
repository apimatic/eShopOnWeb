using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationLineDto
{
    public string Status { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public decimal? EShopAmount { get; set; }
    public string? PayPalTransactionId { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? PayPalStatus { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class ReconciliationReportDto
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int MissingInEShopCount { get; set; }
    public int MissingInPayPalCount { get; set; }
    public List<ReconciliationLineDto> Lines { get; set; } = new();
}

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report listing PayPal's own transaction
/// record for the range and lining it up against eShop orders across the WHOLE range. from/to are
/// ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IReconciliationService service, CancellationToken ct) =>
            {
                if (to < from)
                {
                    return Results.BadRequest(new { message = "'to' must be on or after 'from'." });
                }

                var report = await service.ReconcileAsync(from, to, ct);

                var dto = new ReconciliationReportDto
                {
                    From = report.From,
                    To = report.To,
                    MatchedCount = report.MatchedCount,
                    MissingInEShopCount = report.MissingInEShopCount,
                    MissingInPayPalCount = report.MissingInPayPalCount,
                    Lines = report.Lines.Select(l => new ReconciliationLineDto
                    {
                        Status = l.Status.ToString(),
                        Reference = l.Reference,
                        OrderId = l.OrderId,
                        EShopAmount = l.EShopAmount,
                        PayPalTransactionId = l.PayPalTransactionId,
                        PayPalAmount = l.PayPalAmount,
                        PayPalStatus = l.PayPalStatus,
                        Currency = l.Currency,
                    }).ToList(),
                };

                return Results.Ok(dto);
            })
            .Produces<ReconciliationReportDto>()
            .WithTags("OrderEndpoints");
    }
}
