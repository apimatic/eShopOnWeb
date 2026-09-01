using System.Linq;
using System.Security.Claims;
using System.Threading;
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

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IPaymentService _paymentService;
    private readonly PayPalSettings _payPalSettings;

    public CreateOrderEndpoint(IPaymentService paymentService, PayPalSettings payPalSettings)
    {
        _paymentService = paymentService;
        _payPalSettings = payPalSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "items must contain at least one entry." });
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "every item quantity must be at least 1." });
        }

        var items = request.Items
            .Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity))
            .ToList();
        var address = request.ShipToAddress?.ToModel() ?? ShippingAddressDto.Default();

        var order = await _paymentService.CreateOrderAsync(buyerId, items, address, CancellationToken.None);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = _payPalSettings.Currency,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
