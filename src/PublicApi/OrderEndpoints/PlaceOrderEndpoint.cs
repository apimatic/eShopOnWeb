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

public class PlaceOrderRequest : BaseRequest
{
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
}

public class PlaceOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderFlowService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, HttpContext http, IOrderFlowService orders) =>
            {
                var buyerId = http.User.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, buyerId, orders, http.RequestAborted);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderFlowService orders) =>
        HandleAsync(request, string.Empty, orders, default);

    private static async Task<IResult> HandleAsync(
        PlaceOrderRequest request,
        string buyerId,
        IOrderFlowService orders,
        System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            var lines = (request.Items ?? new List<PlaceOrderItemRequest>())
                .Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity))
                .ToList();
            var order = await orders.PlaceOrderAsync(buyerId, lines, cancellationToken);
            var response = new PlaceOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (System.Exception ex)
        {
            return EndpointErrors.FromException(ex);
        }
    }
}
