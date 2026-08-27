using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }
                if (request.Items is null || request.Items.Count == 0)
                {
                    return Results.BadRequest("An order must contain at least one item.");
                }

                var items = request.Items.Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity)).ToList();
                var address = request.ShipToAddress is null
                    ? null
                    : new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
                        request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

                var order = await orderPaymentService.CreateOrderAsync(buyerId, items, address, ct);

                var response = new CreateOrderResponse(request.CorrelationId())
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    Total = order.Total(),
                    Items = order.OrderItems.Select(i => new CreateOrderItemDto
                    {
                        CatalogItemId = i.ItemOrdered.CatalogItemId,
                        Name = i.ItemOrdered.ProductName,
                        UnitPrice = i.UnitPrice,
                        Units = i.Units
                    }).ToList()
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
