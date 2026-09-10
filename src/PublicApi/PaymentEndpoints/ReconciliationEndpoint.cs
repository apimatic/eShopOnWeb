using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record ReconciliationQuery(DateTimeOffset From, DateTimeOffset To, CancellationToken Ct);

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report lining PayPal's own transaction
/// records up against eShop orders over an ISO-8601 date-time range. Covers the whole range.
/// Administrator-only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, HttpContext http, IPaymentApplicationService service) =>
                await HandleAsync(new ReconciliationQuery(from, to, http.RequestAborted), service))
            .Produces<ReconciliationReportDto>()
            .WithTags("Reconciliation");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery query, IPaymentApplicationService service)
    {
        var report = await service.ReconcileAsync(query.From, query.To, query.Ct);
        return Results.Ok(PaymentMapper.ToDto(report));
    }
}
