using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPaymentService orders) =>
            {
                return await HandleAsync(request, orders);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orders)
    {
        var buyerId = RequireBuyerId(_httpContextAccessor.HttpContext?.User);
        var items = request.Items.Select(i => new CatalogOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        var order = await orders.PlaceOrderAsync(buyerId, items, PaymentApiMapper.ToAddress(request.ShipToAddress));
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = PaymentApiMapper.ToDto(order)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    internal static string RequireBuyerId(ClaimsPrincipal? user)
    {
        var buyerId = user?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new ApplicationCore.Exceptions.PaymentException("The caller is not authenticated.", 401);
        }

        return buyerId;
    }
}
