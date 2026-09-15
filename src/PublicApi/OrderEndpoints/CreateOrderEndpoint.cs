using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IOrderNotificationService orderNotificationService) =>
            {
                return await HandleAsync(request, orderNotificationService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService orderNotificationService)
    {
        var buyerId = EndpointHelpers.GetBuyerId(_httpContextAccessor.HttpContext!);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var items = (request.Items ?? new List<CreateOrderItemRequest>())
            .Select(i => new OrderLineRequest { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity })
            .ToList();

        var result = await orderNotificationService.PlaceOrderAsync(buyerId, items);
        return result.ToHttpResult(order =>
        {
            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        });
    }
}
