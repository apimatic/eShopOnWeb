using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentReconciliationService reconciliation) =>
            {
                var report = await reconciliation.ReconcileAsync(from, to, default);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(IPaymentReconciliationService reconciliation) =>
        Task.FromResult<IResult>(Results.Unauthorized());
}
