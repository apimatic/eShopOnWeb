using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IPaymentReconciliationService service) =>
            {
                var report = await service.ReconcileAsync(from, to);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(DateTimeOffset from, IPaymentReconciliationService service)
        => Task.FromResult(Results.BadRequest());
}
