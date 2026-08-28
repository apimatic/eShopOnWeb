using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public ReconciliationResponse() { }

    public ReconciliationReport? Report { get; set; }
}

/// <summary>
/// Operator action. Lists PayPal's own record of transactions for a date range and lines it up
/// against eShop's orders, so a payment one side knows about and the other does not is visible.
/// The report covers the whole range — it is chunked and paged internally, not just the first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IReconciliationService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReconciliationService reconciliationService, HttpContext context) =>
            {
                return await HandleAsync(reconciliationService, context);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(IReconciliationService reconciliationService, HttpContext context)
    {
        if (!TryParseDate(context.Request.Query["from"], out var from))
        {
            return Results.BadRequest(new
            {
                message = "'from' is required and must be an ISO-8601 date-time, e.g. 2026-08-01T00:00:00Z."
            });
        }

        if (!TryParseDate(context.Request.Query["to"], out var to))
        {
            return Results.BadRequest(new
            {
                message = "'to' is required and must be an ISO-8601 date-time, e.g. 2026-08-28T00:00:00Z."
            });
        }

        var report = await reconciliationService.BuildReportAsync(from, to, context.RequestAborted);

        return Results.Ok(new ReconciliationResponse { Report = report });
    }

    private static bool TryParseDate(string? value, out DateTimeOffset parsed)
    {
        parsed = default;

        return !string.IsNullOrWhiteSpace(value) &&
               DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed);
    }
}
