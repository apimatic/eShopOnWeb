using System;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IOrderPaymentService paymentService, HttpContext context) =>
            {
                if (from == null || to == null)
                {
                    return Results.BadRequest(new { message = "Both 'from' and 'to' ISO-8601 date-times are required." });
                }

                var result = await paymentService.GetReconciliationAsync(from.Value, to.Value, context.RequestAborted);
                return Results.Ok(result);
            })
            .Produces<ReconciliationReport>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }
}
