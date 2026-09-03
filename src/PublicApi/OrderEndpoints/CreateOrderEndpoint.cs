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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IShopperOrderService service, ClaimsPrincipal user, HttpContext http) =>
            {
                return await HandleAsync(request, service, user, http.RequestAborted);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IShopperOrderService service) =>
        HandleAsync(request, service, new ClaimsPrincipal(), default);

    private async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        IShopperOrderService service,
        ClaimsPrincipal user,
        System.Threading.CancellationToken cancellationToken)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var lines = request.Items.Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var result = await service.PlaceAsync(buyerId, lines, cancellationToken);
        var dto = OrderDto.From(result);
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = dto.OrderId,
            Order = dto
        };
        return Results.Created($"api/orders/{dto.OrderId}", response);
    }
}
