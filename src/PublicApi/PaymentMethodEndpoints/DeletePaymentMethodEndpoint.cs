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

/// <summary>
/// Remove one of the caller's saved cards. It is disabled at Finix and no longer usable here.
/// </summary>
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
        await paymentService.DeleteSavedCardAsync(user.GetUserName(), paymentMethodId);

        return Results.Ok(new DeletePaymentMethodResponse
        {
            PaymentMethodId = paymentMethodId,
            Deleted = true
        });
    }
}
