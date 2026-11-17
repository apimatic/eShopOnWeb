using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = GetBuyerId(user);
                var items = (request.Items ?? request.BasketItems ?? new())
                    .Select(item => new NewOrderItem
                    {
                        CatalogItemId = item.CatalogItemId,
                        Quantity = item.EffectiveQuantity
                    })
                    .ToList();

                var address = (request.ShipToAddress ?? request.ShippingAddress)?.ToAddress()
                              ?? new AddressRequest().ToAddress();

                var order = await paymentService.CreateOrderAsync(buyerId, items, address, cancellationToken);
                var response = OrderDto.From(order);
                return Results.Created($"api/orders/{response.OrderId}", response);
            })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    internal static string GetBuyerId(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new ApiException(401, "The caller identity could not be determined from the token.");
        }

        return buyerId;
    }
}
