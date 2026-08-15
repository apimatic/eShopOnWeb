using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest
{
    /// <summary>The catalog items and quantities to order. Amounts come from catalog prices, not the caller.</summary>
    [Required]
    public List<CreateOrderLine> Items { get; set; } = new();
}

public class CreateOrderLine
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse
{
    /// <summary>Top-level identifier of the created order, so the flow can be driven end to end.</summary>
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing eShop's
/// existing order/order-item model. The order starts awaiting payment; the caller's identity comes
/// from the token.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PayPalSettings _settings;

    public CreateOrderEndpoint(IOrderPaymentService orderPaymentService,
        IHttpContextAccessor httpContextAccessor, PayPalSettings settings)
    {
        _orderPaymentService = orderPaymentService;
        _httpContextAccessor = httpContextAccessor;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request) => await HandleAsync(request))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetBuyerId();
        var lines = (request.Items ?? new List<CreateOrderLine>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await _orderPaymentService.PlaceOrderAsync(buyerId, lines);
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order, _settings.Currency)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
