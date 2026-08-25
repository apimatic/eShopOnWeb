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

/// <summary>Removes a saved card for the signed-in shopper - it can no longer be used to pay afterwards.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService paymentMethodService) =>
            {
                var request = new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId, BuyerId = user.Identity!.Name! };
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var response = new DeletePaymentMethodResponse(request.CorrelationId());

        var deleted = await paymentMethodService.DeletePaymentMethodAsync(request.BuyerId, request.PaymentMethodId);
        if (!deleted)
        {
            return Results.NotFound();
        }

        return Results.Ok(response);
    }
}
