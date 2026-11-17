using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: lines up Finix transactions for a date range against eShop
/// orders, covering every page Finix returns for the range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from,
             DateTimeOffset to,
             IOrderPaymentService paymentService,
             CancellationToken cancellationToken) =>
            {
                var report = await paymentService.GetReconciliationAsync(from, to, cancellationToken);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("OrderEndpoints");
    }
}
