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

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper. The order starts
/// awaiting payment. Returns the created order id as a top-level field.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _service;
    private readonly PayPalSettings _settings;

    public PlaceOrderEndpoint(IOrderPaymentService service, PayPalSettings settings)
    {
        _service = service;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.GetBuyerId(user);

        var lines = (request.Items ?? new())
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
            .ToList();

        ShippingAddressInput? address = request.ShipToAddress is null
            ? null
            : new ShippingAddressInput(request.ShipToAddress.Street, request.ShipToAddress.City, request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = await _service.PlaceOrderAsync(buyerId, lines, address);

        var response = new PlaceOrderResponse
        {
            OrderId = order.Id,
            Status = "AwaitingPayment",
            Amount = order.Total(),
            Currency = _settings.Currency,
            Items = PaymentResponseMapper.ToLines(order),
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
