using System.Linq;
using System.Security.Claims;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPlacementService, CancellationToken>
{
    // A placeholder shipping address used when the caller does not supply one — this app's focus is
    // payment, not fulfilment, but the Order model requires a ship-to address.
    private static Address DefaultShipToAddress() => new("123 Main St", "Redmond", "WA", "US", "98052");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderPlacementService orderPlacementService,
                CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.SetBuyer(buyerId);
                return await HandleAsync(request, orderPlacementService, ct);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPlacementService orderPlacementService,
        CancellationToken ct)
    {
        var response = new PlaceOrderResponse(request.CorrelationId());

        var address = request.ShipToAddress is null
            ? DefaultShipToAddress()
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City, request.ShipToAddress.State,
                request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var lines = request.Items.Select(i => new OrderLineItem(i.CatalogItemId, i.Quantity)).ToList();

        var result = await orderPlacementService.PlaceOrderAsync(request.BuyerId!, lines, address, ct);

        var failure = ApiResultMapper.MapFailure(result);
        if (failure is not null)
        {
            return failure;
        }

        var order = result.Value;
        response.OrderId = order.Id;
        response.Order = OrderDto.From(order);
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
