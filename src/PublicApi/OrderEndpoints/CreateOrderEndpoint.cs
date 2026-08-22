using System.Collections.Generic;
using System.Security.Claims;
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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderCheckoutService checkoutService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(BindBuyer(request, user), checkoutService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderCheckoutService checkoutService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new CheckoutException("Provide at least one catalog item.", 400);
        }

        var shipTo = request.ShipTo ?? new ShippingAddressDto();
        var address = new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);
        var lines = new List<OrderLineRequest>();
        foreach (var item in request.Items)
        {
            lines.Add(new OrderLineRequest(item.CatalogItemId, item.Quantity));
        }

        var order = await checkoutService.PlaceOrderAsync(request.BuyerId, lines, address, default);
        var body = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderResponseMapper.Map(order)
        };
        return Results.Created($"api/orders/{order.Id}", body);
    }

    private static CreateOrderRequest BindBuyer(CreateOrderRequest request, ClaimsPrincipal user)
    {
        request.BuyerId = user.Identity?.Name ?? string.Empty;
        return request;
    }
}

public class CreateOrderRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressDto? ShipTo { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderResponse Order { get; set; } = new();
}
