using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
    public AddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment and
/// reuses the app's existing order/order-item model. The caller's identity comes from the token.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
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
            (CreateOrderRequest request, IOrderPaymentService orderPaymentService) =>
                await HandleAsync(request, orderPaymentService))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetBuyerId();

        var lines = (request.Items ?? new List<CreateOrderItem>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        ShippingAddressInput? shipTo = request.ShipToAddress == null
            ? null
            : new ShippingAddressInput(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var orderId = await orderPaymentService.PlaceOrderAsync(buyerId, lines, shipTo);

        return Results.Created($"api/orders/{orderId}",
            new CreateOrderResponse { OrderId = orderId, Status = "AwaitingPayment" });
    }
}
