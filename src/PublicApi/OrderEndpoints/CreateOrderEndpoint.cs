using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper, reusing the app's existing
/// Order/OrderItem model. The order starts awaiting payment. Returns the new order id.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                ClaimsPrincipal user,
                IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var items = request.Items
                    .Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity))
                    .ToList();

                var address = request.ShippingAddress is null
                    ? null
                    : new ShippingAddressInput(
                        request.ShippingAddress.Street,
                        request.ShippingAddress.City,
                        request.ShippingAddress.State,
                        request.ShippingAddress.Country,
                        request.ShippingAddress.ZipCode);

                var orderId = await paymentService.PlaceOrderAsync(buyerId, items, address, cancellationToken);
                var order = await paymentService.GetMyOrderAsync(buyerId, orderId, cancellationToken);

                return Results.Created($"api/orders/{orderId}", new { orderId, order });
            })
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }
}
