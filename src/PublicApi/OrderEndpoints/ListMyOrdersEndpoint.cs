using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderPaymentService service, ClaimsPrincipal user) => await HandleAsync(user, service))
            .Produces<OrderResponse[]>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderPaymentService service)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
        var orders = await service.ListMyOrdersAsync(buyerId);
        return Results.Ok(orders.Select(PaymentEndpointHelpers.ToResponse).ToList());
    }
}
