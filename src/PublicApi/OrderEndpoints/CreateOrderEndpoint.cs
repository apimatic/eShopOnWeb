using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest>
{
    private readonly IShopperOrderService _orders;

    public CreateOrderEndpoint(IShopperOrderService orders)
    {
        _orders = orders;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext) =>
            {
                var unauthorized = HttpCaller.UnauthorizedIfAnonymous(httpContext);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                request.BuyerId = HttpCaller.BuyerId(httpContext)!;
                request.CancellationToken = httpContext.RequestAborted;
                return await HandleAsync(request);
            })
            .Produces<CreateOrderResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request)
    {
        try
        {
            var lines = (request.Items ?? new List<CreateOrderItemRequest>())
                .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                .ToList();
            var order = await _orders.PlaceAsync(request.BuyerId, lines, request.CancellationToken);
            return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    internal string BuyerId { get; set; } = string.Empty;
    internal CancellationToken CancellationToken { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
