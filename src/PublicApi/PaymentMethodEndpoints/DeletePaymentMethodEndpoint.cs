using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IPaymentMethodService service, HttpContext httpContext) =>
            {
                var buyerId = BuyerIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId, BuyerId = buyerId }, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService service)
    {
        await service.DeleteAsync(request.BuyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}
