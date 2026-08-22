using System;
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

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliation, CancellationToken cancellationToken) =>
            {
                var report = await reconciliation.ReconcileAsync(from, to, cancellationToken);
                var response = new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    Matched = report.Matched.ToList(),
                    PayPalOnly = report.PayPalOnly.ToList(),
                    EshopOnly = report.EshopOnly.ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(IReconciliationService reconciliation)
    {
        throw new InvalidOperationException("Use the routed handler with from/to query parameters.");
    }
}
