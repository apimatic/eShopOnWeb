using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To, CancellationToken Ct);

/// <summary>
/// GET /api/reconciliation?from=&amp;to= — operator report lining PayPal's transaction record up against
/// eShop orders over an ISO-8601 date-time range (covers the whole range, not just the first page).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset from, DateTimeOffset to, IReconciliationService service, CancellationToken ct) =>
                await HandleAsync(new ReconciliationRequest(from, to, ct), service))
            .Produces<ReconciliationReport>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To, request.Ct);
        return Results.Ok(report);
    }
}
