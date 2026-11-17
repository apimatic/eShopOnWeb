using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, HttpContext, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, HttpContext httpContext, IPaymentService paymentService) =>
            {
                return await HandleAsync(request, httpContext, paymentService);
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status402PaymentRequired)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, HttpContext httpContext, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.RequireBuyer(httpContext);
        var paymentMethod = await paymentService.SavePaymentMethodAsync(buyerId, request, httpContext.RequestAborted);
        return Results.Created($"/api/payment-methods/{paymentMethod.PaymentMethodId}", paymentMethod);
    }
}
