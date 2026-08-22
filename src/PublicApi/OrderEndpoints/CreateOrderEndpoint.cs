using System.Linq;
using System.Security.Claims;
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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ICheckoutPaymentService checkout, ClaimsPrincipal user) =>
            {
                var buyerId = OrderEndpointHelpers.GetBuyerId(user);
                var shipTo = request.ShipTo == null
                    ? null
                    : new Address(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

                var items = (request.Items ?? Enumerable.Empty<CreateOrderItemRequest>())
                    .Select(i => (i.CatalogItemId, i.Quantity))
                    .ToList();

                var order = await checkout.PlaceOrderAsync(buyerId, items, shipTo);
                var response = new CreateOrderResponse
                {
                    OrderId = order.Id,
                    Order = OrderEndpointHelpers.ToDto(order)
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutPaymentService checkout)
    {
        return Task.FromResult(Results.BadRequest());
    }
}
