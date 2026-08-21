using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentService;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.BuyerId = BuyerIdentity.GetBuyerId(user);
                return await HandleAsync(request, paymentService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService paymentService)
    {
        var items = (request.Items ?? new List<OrderItemDto>())
            .Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = request.ShipToAddress is { } a
            ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : new Address("N/A", "N/A", "N/A", "US", "00000");

        var orderId = await paymentService.PlaceOrderAsync(request.BuyerId, address, items);

        return Results.Created($"api/orders/{orderId}", new CreateOrderResponse { OrderId = orderId });
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }

    /// <summary>Set from the token, never from the request body.</summary>
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
}
