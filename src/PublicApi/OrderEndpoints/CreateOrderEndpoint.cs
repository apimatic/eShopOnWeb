using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the authenticated shopper. The order reuses the app's
/// standard Order/OrderItem model and starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext http)
    {
        var buyerId = CallerIdentity.GetBuyerId(http.User);
        var orderService = http.RequestServices.GetRequiredService<IOrderService>();
        var cancellationToken = http.RequestAborted;

        var items = (request.Items ?? new List<OrderItemRequest>())
            .Select(i => new OrderItemInput(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await orderService.CreateOrderAsync(buyerId, items, ToAddress(request.ShipToAddress), cancellationToken);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = "USD"
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address ToAddress(ShippingAddressRequest? shipTo)
    {
        if (shipTo is null)
        {
            // Payment is the focus of this API; use a placeholder address so the standard Order model
            // (which requires a ship-to address) is satisfied.
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        }

        return new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);
    }
}
