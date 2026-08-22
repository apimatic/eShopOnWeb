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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderNotificationService service, HttpContext http) =>
            {
                var buyerId = http.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.BuyerId = buyerId;
                request.CancellationToken = http.RequestAborted;
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service)
    {
        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        ShipToAddress? shipTo = request.ShipTo is null
            ? null
            : new ShipToAddress(
                request.ShipTo.Street,
                request.ShipTo.City,
                request.ShipTo.State,
                request.ShipTo.Country,
                request.ShipTo.ZipCode);

        var order = await service.PlaceOrderAsync(request.BuyerId, lines, shipTo, request.CancellationToken);
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
