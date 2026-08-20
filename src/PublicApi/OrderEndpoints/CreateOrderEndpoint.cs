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

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipTo { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderResponse Order { get; set; } = new();
}

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPaymentService service, HttpContext http) =>
            {
                return await HandleAsync(request, service, http);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
        => HandleAsync(request, service, http: null!);

    private async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service, HttpContext http)
    {
        var items = request.Items.ConvertAll(i => new PlaceOrderItem
        {
            CatalogItemId = i.CatalogItemId,
            Quantity = i.Quantity
        });

        var order = await service.PlaceOrderAsync(
            http.RequireBuyerId(),
            items,
            OrderResponseMapper.ToAddress(request.ShipTo),
            http.RequestAborted);

        var mapped = OrderResponseMapper.Map(order);
        var response = new CreateOrderResponse
        {
            OrderId = mapped.OrderId,
            Order = mapped
        };
        return Results.Created($"api/orders/{mapped.OrderId}", response);
    }
}
