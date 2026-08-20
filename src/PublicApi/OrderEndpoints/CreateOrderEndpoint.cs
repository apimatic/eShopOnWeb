using System;
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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IShopperOrderService>
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
            (CreateOrderRequest request, IShopperOrderService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IShopperOrderService service)
    {
        try
        {
            var buyerId = _httpContextAccessor.HttpContext!.RequireUserName();
            var items = (request.Items ?? new List<CreateOrderItemRequest>()).Select(i => new CatalogOrderItemRequest(i.CatalogItemId, i.Quantity)).ToList();
            var order = await service.PlaceOrderAsync(buyerId, items);
            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (Exception ex)
        {
            return ex.ToResult();
        }
    }
}
