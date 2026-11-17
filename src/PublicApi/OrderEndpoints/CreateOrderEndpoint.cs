using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public AddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    private readonly IPaymentService _paymentService;

    public CreateOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (CreateOrderRequest request, HttpContext httpContext) => await HandleAsync(request, httpContext))
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        var items = request.Items ?? new List<OrderItemRequest>();
        var data = new CreateOrderData(buyerId,
            items.Select(item => new OrderItemInput(item.CatalogItemId, item.Quantity)).ToList(),
            request.ShipToAddress.ToAddressData());
        var order = await _paymentService.CreateOrderAsync(data, httpContext.RequestAborted);
        return Results.Created($"api/orders/{order.OrderId}", order);
    }
}
