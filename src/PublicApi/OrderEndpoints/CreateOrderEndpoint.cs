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
using Microsoft.eShopWeb.ApplicationCore.Payment;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext, IOrderPaymentService orderPaymentService) =>
            {
                return await HandleAsync(WithBuyer(request, httpContext), orderPaymentService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var buyerId = request.BuyerId;
        var lines = request.Items.Select(i => new OrderLineRequest
        {
            CatalogItemId = i.CatalogItemId,
            Quantity = i.Quantity
        }).ToList();

        Address? shipTo = null;
        if (request.ShipToAddress is not null)
        {
            shipTo = new Address(
                request.ShipToAddress.Street ?? string.Empty,
                request.ShipToAddress.City ?? string.Empty,
                request.ShipToAddress.State ?? string.Empty,
                request.ShipToAddress.Country ?? string.Empty,
                request.ShipToAddress.ZipCode ?? string.Empty);
        }

        var order = await orderPaymentService.PlaceOrderAsync(buyerId, lines, shipTo);
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static CreateOrderRequest WithBuyer(CreateOrderRequest request, HttpContext httpContext)
    {
        request.BuyerId = httpContext.User.Identity?.Name
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? string.Empty;
        return request;
    }
}

public partial class CreateOrderRequest
{
    internal string BuyerId { get; set; } = string.Empty;
}
