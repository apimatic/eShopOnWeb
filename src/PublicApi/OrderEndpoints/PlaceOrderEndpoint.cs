using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, HttpContext httpContext, IOrderNotificationService service) =>
            {
                request.BuyerId = httpContext.User.Identity?.Name;
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var result = await service.PlaceOrderAsync(
            request.BuyerId,
            OrderResponseMapper.ToItems(request.Items),
            OrderResponseMapper.ToAddress(request.ShipToAddress));

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = result.Order.Id,
            Status = result.Order.Status.ToString(),
            Total = result.Order.Total(),
            Notifications = result.Notifications.Select(OrderNotificationDto.From).ToList()
        };

        return Results.Created($"api/orders/{result.Order.Id}", response);
    }
}
