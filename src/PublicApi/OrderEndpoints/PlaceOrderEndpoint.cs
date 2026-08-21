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

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, HttpContext httpContext, IShopperOrderService service) =>
            {
                var unauthorized = BuyerIdentity.RequireBuyer(httpContext.User, out var buyerId);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                request.BuyerId = buyerId;
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IShopperOrderService service)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one catalog item is required." });
        }

        try
        {
            Address? address = null;
            if (request.ShipToAddress is not null)
            {
                address = new Address(
                    request.ShipToAddress.Street,
                    request.ShipToAddress.City,
                    request.ShipToAddress.State,
                    request.ShipToAddress.Country,
                    request.ShipToAddress.ZipCode);
            }

            var items = request.Items.Select(i => new CatalogQuantity(i.CatalogItemId, i.Quantity)).ToList();
            var order = await service.PlaceOrderAsync(request.BuyerId, items, address);
            var response = new PlaceOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
