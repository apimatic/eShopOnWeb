using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// Returns the new order's id as a top-level <c>orderId</c>.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext context) => await HandleAsync(request, context))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext context)
    {
        var response = new CreateOrderResponse(request.CorrelationId());
        var placementService = context.RequestServices.GetRequiredService<IOrderPlacementService>();

        var lines = (request.Items ?? new List<OrderItemRequest>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var shipTo = request.ShipToAddress is null
            ? null
            : new ShippingAddress(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var placed = await placementService.PlaceOrderAsync(context.User.BuyerId(), lines, shipTo);

        response.OrderId = placed.Order.Id;
        response.Status = placed.Payment.Status.ToString();
        response.Currency = placed.Payment.CurrencyCode;
        response.Total = placed.Order.Total();
        response.Items = placed.Order.OrderItems
            .Select(oi => new OrderLineResponse
            {
                CatalogItemId = oi.ItemOrdered.CatalogItemId,
                ProductName = oi.ItemOrdered.ProductName,
                UnitPrice = oi.UnitPrice,
                Units = oi.Units
            })
            .ToList();

        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}
