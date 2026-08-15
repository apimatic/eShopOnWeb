using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// POST /api/orders — place an order from catalog items for the signed-in shopper. The order starts
/// awaiting payment. Returns the new order id as a top-level field.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public CreateOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPaymentService service) =>
                await HandleAsync(request, service))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        var buyerId = CurrentUser.RequireBuyerId(_http);

        var lines = (request.Items ?? new()).Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var shipTo = request.ShipTo is null
            ? null
            : new ShippingAddressInput(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State,
                request.ShipTo.Country, request.ShipTo.ZipCode);

        var orderId = await service.PlaceOrderAsync(buyerId, lines, shipTo, CurrentUser.RequestAborted(_http));

        var response = new CreateOrderResponse
        {
            OrderId = orderId,
            PaymentStatus = ApplicationCore.Entities.OrderAggregate.OrderPaymentStatus.AwaitingPayment.ToString()
        };
        return Results.Created($"api/orders/{orderId}", response);
    }
}
