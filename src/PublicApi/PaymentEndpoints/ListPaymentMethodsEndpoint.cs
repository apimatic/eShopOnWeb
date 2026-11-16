using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Lists the signed-in shopper's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (IOrderPaymentService paymentService, ClaimsPrincipal user) =>
                {
                    return await HandleAsync(paymentService, user);
                })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderPaymentService paymentService, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        var methods = await paymentService.GetPaymentMethodsAsync(buyerId!);
        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = methods.ToList()
        };

        return Results.Ok(response);
    }
}
