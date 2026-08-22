using System.Collections.Generic;
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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IOrderPaymentService service, HttpContext httpContext) =>
            {
                request.BuyerId = httpContext.RequireBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        var lines = (request.Items ?? new List<CreateOrderItemRequest>())
            .Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await service.PlaceOrderAsync(request.BuyerId, lines, request.ShipToAddress?.ToInput());
        var dto = OrderDtoMapper.ToDto(order);
        return Results.Created($"api/orders/{dto.OrderId}", new CreateOrderResponse
        {
            OrderId = dto.OrderId,
            Order = dto
        });
    }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}
