using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IOrderPaymentService service) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromDate))
                    return Results.BadRequest(new { error = "'from' must be an ISO-8601 date-time." });
                if (!DateTimeOffset.TryParse(to, out var toDate))
                    return Results.BadRequest(new { error = "'to' must be an ISO-8601 date-time." });
                if (fromDate >= toDate)
                    return Results.BadRequest(new { error = "'from' must be before 'to'." });

                try
                {
                    var report = await service.GetReconciliationAsync(fromDate, toDate);
                    return Results.Ok(report);
                }
                catch (Exception ex) { return Results.Problem(ex.Message); }
            })
            .Produces(200)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderPaymentService service)
        => await Task.FromResult(Results.StatusCode(501));
}
