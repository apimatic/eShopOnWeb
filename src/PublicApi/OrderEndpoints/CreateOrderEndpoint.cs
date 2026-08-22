using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderFlowService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IOrderFlowService service, HttpContext http) =>
            {
                return await HandleAsync(request, service, http);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderFlowService service)
        => HandleAsync(request, service, null!);

    private async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderFlowService service, HttpContext http)
    {
        var unauthorized = http.RequireBuyerId(out var buyerId);
        if (unauthorized != null)
        {
            return unauthorized;
        }

        try
        {
            Address? address = null;
            if (request.ShipTo != null)
            {
                address = new Address(
                    request.ShipTo.Street,
                    request.ShipTo.City,
                    request.ShipTo.State,
                    request.ShipTo.Country,
                    request.ShipTo.ZipCode);
            }

            var items = request.Items.Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity)).ToList();
            var order = await service.PlaceOrderAsync(buyerId, new PlaceOrderRequest(items, address));
            var response = new CreateOrderResponse(request.CorrelationId())
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
        catch (KeyNotFoundException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
