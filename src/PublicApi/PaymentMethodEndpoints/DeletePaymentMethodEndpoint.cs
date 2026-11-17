using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IPaymentService _paymentService;

    public DeletePaymentMethodEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int paymentMethodId, HttpContext httpContext) =>
                    await HandleAsync(paymentMethodId, httpContext))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, HttpContext httpContext)
    {
        await _paymentService.DeletePaymentMethodAsync(paymentMethodId,
            httpContext.User.GetBuyerId(),
            httpContext.RequestAborted);
        return Results.NoContent();
    }
}
