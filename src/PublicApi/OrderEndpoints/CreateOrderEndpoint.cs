using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper, reusing the app's existing
/// order/order-item model. The shopper is told their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, [FromServices] IOrderNotificationService service, ClaimsPrincipal user) =>
                await HandleAsync(request, service, user))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service, ClaimsPrincipal user)
    {
        var userName = user.GetUserName();
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }
        if (request?.Items == null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Units)).ToList();
        var address = BuildAddress(request.ShipToAddress);

        var result = await service.PlaceOrderAsync(userName, lines, address);
        if (result.Order is null)
        {
            return Results.BadRequest(new { message = result.Error ?? "The order could not be placed." });
        }

        var order = result.Order;
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems.Select(oi => new OrderItemDto
            {
                CatalogItemId = oi.ItemOrdered.CatalogItemId,
                ProductName = oi.ItemOrdered.ProductName,
                UnitPrice = oi.UnitPrice,
                Units = oi.Units
            }).ToList()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(ShipToAddressDto? dto)
    {
        if (dto == null)
        {
            return new Address("Not provided", "Not provided", string.Empty, "Not provided", "00000");
        }
        return new Address(
            string.IsNullOrWhiteSpace(dto.Street) ? "Not provided" : dto.Street,
            string.IsNullOrWhiteSpace(dto.City) ? "Not provided" : dto.City,
            dto.State ?? string.Empty,
            string.IsNullOrWhiteSpace(dto.Country) ? "Not provided" : dto.Country,
            string.IsNullOrWhiteSpace(dto.ZipCode) ? "00000" : dto.ZipCode);
    }
}
