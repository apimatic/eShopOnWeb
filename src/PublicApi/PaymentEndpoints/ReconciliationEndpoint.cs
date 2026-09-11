using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record ReconciliationQuery(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and lines them up
/// against eShop orders, surfacing payments PayPal knows about that eShop does not — and the reverse.
/// Covers the whole range (chunking and paging inside the PayPal client).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentService paymentService) =>
                await HandleAsync(new ReconciliationQuery(from, to), paymentService))
            .Produces<ReconciliationReport>()
            .WithTags("Reconciliation");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IPaymentService paymentService)
    {
        var report = await paymentService.ReconcileAsync(request.From, request.To);
        return Results.Ok(report);
    }
}
