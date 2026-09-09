using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator action: PayPal's own record of transactions over a date range, lined up against eShop
/// orders. Covers the whole range (all pages, chunked into PayPal's 31-day windows). Restricted to
/// the administrator role. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, CancellationToken ct, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationQuery(from, to, ct), service);
            })
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IReconciliationService service)
    {
        if (request.To < request.From)
        {
            throw new InvalidPaymentOperationException("'to' must not be earlier than 'from'.");
        }

        var report = await service.ReconcileAsync(request.From, request.To, request.Cancellation);
        return Results.Ok(report);
    }
}

public record ReconciliationQuery(DateTimeOffset From, DateTimeOffset To, CancellationToken Cancellation);
