using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, string, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IReconciliationService service, CancellationToken ct) =>
            {
                var report = await service.ReconcileAsync(from, to, ct);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(string request, IReconciliationService service) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}
