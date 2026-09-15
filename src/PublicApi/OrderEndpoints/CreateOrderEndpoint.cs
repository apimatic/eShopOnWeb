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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
{
    private readonly IOrderNotificationService _notifications;

    public CreateOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext httpContext, IOrderService orders) =>
            {
                var buyerId = httpContext.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, buyerId, orders);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService) =>
        throw new System.NotSupportedException();

    private async Task<IResult> HandleAsync(CreateOrderRequest request, string buyerId, IOrderService orderService)
    {
        try
        {
            var addressRequest = request.ShipToAddress ?? new CreateOrderAddressRequest();
            var address = new Address(
                addressRequest.Street,
                addressRequest.City,
                addressRequest.State,
                addressRequest.Country,
                addressRequest.ZipCode);

            var items = (request.Items ?? []).Select(i => new CatalogQuantity(i.CatalogItemId, i.Quantity)).ToList();
            var order = await orderService.PlaceOrderAsync(buyerId, items, address);
            await _notifications.NotifyOrderPlacedAsync(order.Id, buyerId);

            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (EmptyBasketOnCheckoutException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (CatalogItemNotFoundException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
