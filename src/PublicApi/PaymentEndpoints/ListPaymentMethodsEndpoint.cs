using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, HttpContext, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IPaymentService paymentService) =>
            {
                return await HandleAsync(httpContext, paymentService);
            })
            .Produces<PaymentMethodDto[]>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.RequireBuyer(httpContext);
        var paymentMethods = await paymentService.GetPaymentMethodsAsync(buyerId, httpContext.RequestAborted);
        return Results.Ok(paymentMethods);
    }
}
