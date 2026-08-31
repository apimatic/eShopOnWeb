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

/// <summary>
/// Places an order from catalog items for the signed-in shopper.
/// The order starts in a state awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPaymentService orderPaymentService, HttpContext httpContext) =>
            {
                request.BuyerId = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var lines = request.Items
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? address = request.ShipToAddress == null
            ? null
            : new Address(
                request.ShipToAddress.Street,
                request.ShipToAddress.City,
                request.ShipToAddress.State,
                request.ShipToAddress.Country,
                request.ShipToAddress.ZipCode);

        var order = await orderPaymentService.CreateOrderAsync(request.BuyerId, lines, address);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Total = order.Total();
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
