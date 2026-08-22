using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Auth;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderNotificationService orders) =>
            {
                return await HandleAsync(request, user, orders);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService orders)
        => HandleAsync(request, new ClaimsPrincipal(), orders);

    public async Task<IResult> HandleAsync(
        PlaceOrderRequest request,
        ClaimsPrincipal user,
        IOrderNotificationService orders)
    {
        try
        {
            var lines = (request.Items ?? new()).Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity)).ToList();
            var order = await orders.PlaceOrderAsync(HttpUser.GetBuyerId(user), lines, shipToAddress: null);
            var response = new PlaceOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
