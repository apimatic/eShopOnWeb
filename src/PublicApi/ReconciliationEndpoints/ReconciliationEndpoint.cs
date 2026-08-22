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

public class ReconciliationEndpoint : IEndpoint<IResult, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, ICheckoutPaymentService payments) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromDate) ||
                    !DateTimeOffset.TryParse(to, out var toDate))
                {
                    return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
                }

                var report = await payments.ReconcileAsync(fromDate, toDate);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(ICheckoutPaymentService payments) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}
