using System;
using System.Collections.Generic;
using System.Security.Claims;
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

public record CreateOrderRequest(IReadOnlyList<OrderItemInputDto> Items);
public record OrderItemInputDto(int CatalogItemId, int Quantity);
public record CreateOrderResponse(int OrderId, string BuyerId);

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentService, IHttpContextAccessor>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IPaymentService svc, IHttpContextAccessor ctx) =>
                await HandleAsync(request, svc, ctx))
            .Produces<CreateOrderResponse>(201)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService svc, IHttpContextAccessor ctx)
    {
        var buyerId = ctx.HttpContext!.User.FindFirstValue(ClaimTypes.Email)
            ?? ctx.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.HttpContext.User.Identity?.Name
            ?? throw new UnauthorizedAccessException();

        var items = new List<ApplicationCore.Interfaces.OrderItemInput>();
        foreach (var i in request.Items)
            items.Add(new ApplicationCore.Interfaces.OrderItemInput { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity });

        var order = await svc.CreateOrderAsync(buyerId, items);
        return Results.Created($"/api/orders/{order.Id}", new CreateOrderResponse(order.Id, order.BuyerId));
    }
}
