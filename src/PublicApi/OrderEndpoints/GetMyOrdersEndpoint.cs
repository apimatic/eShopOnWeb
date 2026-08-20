using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersEndpoint : IEndpoint<IResult, string, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ICheckoutPaymentService service, CancellationToken ct) =>
            {
                var orders = await service.GetMyOrdersAsync(user.GetBuyerId(), ct);
                return Results.Ok(orders.Select(OrderResponse.From).ToList());
            })
            .Produces<System.Collections.Generic.List<OrderResponse>>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(string request, ICheckoutPaymentService service) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}
