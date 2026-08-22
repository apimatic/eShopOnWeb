using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ShopOrderEndpoints;

public class CreateShopOrderEndpoint : IEndpoint<IResult, CreateShopOrderRequest, ClaimsPrincipal>
{
    private readonly IShopOrderService _orders;

    public CreateShopOrderEndpoint(IShopOrderService orders)
    {
        _orders = orders;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateShopOrderRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreateShopOrderResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("ShopOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateShopOrderRequest request, ClaimsPrincipal user)
    {
        var unauthorized = EndpointIdentity.RequireBuyer(user, out var buyerId);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        try
        {
            var lines = (request.Items ?? []).Select(i => new ShopOrderLine(i.CatalogItemId, i.Quantity)).ToList();
            var order = await _orders.PlaceAsync(buyerId, lines, default);
            var response = new CreateShopOrderResponse(request.CorrelationId()) { OrderId = order.Id };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (EmptyBasketOnCheckoutException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
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
