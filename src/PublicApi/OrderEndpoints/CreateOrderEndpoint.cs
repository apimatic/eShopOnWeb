using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext httpContext, IOrderPaymentService paymentService) =>
            {
                request.BuyerId = PaymentHttp.BuyerId(httpContext);
                return await HandleAsync(request, paymentService);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService paymentService)
    {
        try
        {
            var lines = request.Items ?? new List<CreateOrderItemRequest>();
            var order = await paymentService.PlaceOrderAsync(
                request.BuyerId,
                lines.ConvertAll(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)),
                (request.ShipToAddress ?? new AddressRequest()).ToAddress());

            return Results.Created($"api/orders/{order.Id}", OrderResponse.From(order));
        }
        catch (System.Exception ex)
        {
            return PaymentHttp.FromException(ex);
        }
    }
}

public class CreateOrderRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public List<CreateOrderItemRequest>? Items { get; set; }
    public AddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
