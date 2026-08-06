using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderService orderService) =>
            {
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
    {
        var buyerId = BuyerIdAccessor.GetBuyerId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { message = "An order must contain at least one item." });

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var shipToAddress = BuildAddress(request.ShipToAddress);

        Order order;
        try
        {
            order = await orderService.CreateOrderAsync(buyerId, lines, shipToAddress);
        }
        catch (System.ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = order.ToDto()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(ShipToAddressRequest? address)
    {
        if (address is not null
            && !string.IsNullOrWhiteSpace(address.Street)
            && !string.IsNullOrWhiteSpace(address.City)
            && !string.IsNullOrWhiteSpace(address.Country)
            && !string.IsNullOrWhiteSpace(address.ZipCode))
        {
            return new Address(address.Street, address.City, address.State ?? string.Empty, address.Country, address.ZipCode);
        }

        // Placeholder shipping address: the model requires one, but shipping is not the focus of this flow.
        return new Address("N/A", "N/A", "N/A", "N/A", "00000");
    }
}
