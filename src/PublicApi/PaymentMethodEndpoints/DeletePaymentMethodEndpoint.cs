using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Removes a saved card. Afterwards it no longer appears in the caller's saved cards and can no longer be used to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, BuyerContext<IPaymentMethodService>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService paymentMethodService) =>
            {
                var context = new BuyerContext<IPaymentMethodService>(user.Identity!.Name!, paymentMethodService);
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId), context);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, BuyerContext<IPaymentMethodService> context)
    {
        var deleted = await context.Service.DeleteAsync(context.BuyerId, request.PaymentMethodId, default);
        if (!deleted)
        {
            return Results.NotFound();
        }

        return Results.Ok(new DeletePaymentMethodResponse(request.CorrelationId()));
    }
}
