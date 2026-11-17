using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, HttpContext, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, HttpContext httpContext, IPaymentService paymentService) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, httpContext, paymentService);
            })
            .Produces<ReconciliationDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithTags("Reconciliation");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, HttpContext httpContext, IPaymentService paymentService)
    {
        var report = await paymentService.ReconcileAsync(request.From, request.To, httpContext.RequestAborted);
        return Results.Ok(report);
    }
}
