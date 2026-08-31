using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the authenticated shopper from catalog item ids and quantities, reusing the
/// app's existing order/order-item model. Returns the new <c>orderId</c> so a bill can then be
/// raised against it.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPlacementService>
{
    // The storefront checkout uses a fixed sample address; mirror it when the caller supplies none.
    private static readonly ShippingAddressDto DefaultAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPlacementService placementService) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, buyerId, placementService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPlacementService placementService)
        => HandleAsync(request, string.Empty, placementService);

    private static async Task<IResult> HandleAsync(CreateOrderRequest request, string buyerId, IOrderPlacementService placementService)
    {
        var address = request.ShipToAddress ?? DefaultAddress;
        var shipTo = new Address(address.Street, address.City, address.State, address.Country, address.ZipCode);

        var lines = (request.Items ?? new())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var result = await placementService.PlaceOrderAsync(buyerId, lines, shipTo);

        return InvoiceApiResults.ToHttp(result, order => Results.Created($"api/orders/{order.Id}", new CreateOrderResponse
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Total = order.Total(),
            Currency = InvoicingConstants.Currency,
            Items = order.OrderItems.Select(oi => new CreateOrderResponseItem(
                oi.ItemOrdered.CatalogItemId, oi.ItemOrdered.ProductName, oi.UnitPrice, oi.Units)).ToList()
        }));
    }
}
