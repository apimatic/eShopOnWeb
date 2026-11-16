using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (CreateOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
                    await HandleAsync(request, user, paymentService))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user,
        IPaymentService paymentService)
    {
        var address = request.ShippingAddress ?? request.ShipToAddress;
        var items = request.Items ?? new System.Collections.Generic.List<CreateOrderItemRequest>();
        var command = new CreateOrderCommand(
            user.GetBuyerId(),
            items.Select(i => new OrderLineCommand(i.CatalogItemId, i.Quantity)).ToList(),
            address is null
                ? null
                : new ShippingAddressCommand(address.Street, address.City, address.State, address.Country,
                    address.ZipCode));

        var order = await paymentService.CreateOrderAsync(command);
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = PaymentMapping.ToOrderResponse(order)
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
