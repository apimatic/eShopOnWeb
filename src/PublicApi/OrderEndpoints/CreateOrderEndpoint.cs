using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext, IShopperOrderService orderService) =>
            {
                var buyerId = EndpointIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var shipping = request.ShippingAddress ?? new ShippingAddressDto();
                var address = new Address(shipping.Street, shipping.City, shipping.State, shipping.Country, shipping.ZipCode);
                var lines = request.Items
                    .Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity))
                    .ToList();

                try
                {
                    var result = await orderService.PlaceAsync(buyerId, lines, address, httpContext.RequestAborted);
                    var response = new CreateOrderResponse(request.CorrelationId())
                    {
                        OrderId = result.Order.Id,
                        Status = result.Order.Status.ToString(),
                        Notification = NotificationMapper.ToDto(result.Notification)
                    };
                    return Results.Created($"api/orders/{result.Order.Id}", response);
                }
                catch (CatalogItemNotFoundException ex)
                {
                    return Results.NotFound(new { message = ex.Message });
                }
                catch (EmptyBasketOnCheckoutException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IShopperOrderService orderService)
        => Task.FromResult(Results.Ok());
}
