using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Removes one of the signed-in shopper's saved cards.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int paymentMethodId, IOrderPaymentService paymentService, ClaimsPrincipal user) =>
                {
                    return await HandleAsync(paymentMethodId, paymentService, user);
                })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, IOrderPaymentService paymentService,
        ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        await paymentService.DeletePaymentMethodAsync(buyerId!, paymentMethodId);
        var response = new DeletePaymentMethodResponse
        {
            PaymentMethodId = paymentMethodId,
            Deleted = true
        };

        return Results.Ok(response);
    }
}
