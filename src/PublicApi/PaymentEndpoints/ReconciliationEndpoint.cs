using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator action. Lists PayPal's own record of
/// transactions for the range and lines them up against eShop orders, surfacing discrepancies in
/// both directions. Covers the whole range (chunked + fully paged). Dates are ISO-8601.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationEndpoint.Request, IReconciliationService>
{
    public record Request(DateTimeOffset From, DateTimeOffset To);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IReconciliationService reconciliationService) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromDate) || !DateTimeOffset.TryParse(to, out var toDate))
                {
                    throw new PaymentException("'from' and 'to' must be valid ISO-8601 date-times.", PaymentErrorReason.Validation);
                }
                return await HandleAsync(new Request(fromDate, toDate), reconciliationService);
            })
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IReconciliationService reconciliationService)
    {
        var report = await reconciliationService.BuildReportAsync(request.From, request.To);
        return Results.Ok(report);
    }
}
