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

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, PaymentMethodIdRequest, HttpContext, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Guid paymentMethodId, HttpContext httpContext, IPaymentService paymentService) =>
            {
                return await HandleAsync(new PaymentMethodIdRequest(paymentMethodId), httpContext, paymentService);
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(PaymentMethodIdRequest request, HttpContext httpContext, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.RequireBuyer(httpContext);
        await paymentService.DeletePaymentMethodAsync(buyerId, request.PaymentMethodId, httpContext.RequestAborted);
        return Results.Ok(new { paymentMethodId = request.PaymentMethodId, status = "Deleted" });
    }
}
