using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Places an order for the signed-in shopper from catalog items. The order starts awaiting payment.</summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http, IOrderPaymentService service) =>
                await HandleAsync(request, http, service))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext http, IOrderPaymentService service) =>
        PaymentApiHelpers.RunAsync(http, async buyerId =>
        {
            var lines = request.Items
                .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
                .ToList();
            var address = PaymentApiHelpers.BuildAddress(request.ShipToAddress);

            var orderId = await service.CreateOrderAsync(buyerId, lines, address, http.RequestAborted);

            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = orderId,
                PaymentStatus = PaymentStatus.AwaitingPayment.ToString()
            };
            return Results.Created($"api/orders/{orderId}", response);
        });
}
