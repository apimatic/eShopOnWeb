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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record MyOrderDto(int OrderId, string Status, string? PayPalOrderId, string? AuthorizationId, string? CaptureId);
public record MyOrdersResponse(IReadOnlyList<MyOrderDto> Orders);

public class MyOrdersEndpoint : IEndpoint<IResult, IPaymentService, IHttpContextAccessor>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/my",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IPaymentService svc, IHttpContextAccessor ctx) => await HandleAsync(svc, ctx))
            .Produces<MyOrdersResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(IPaymentService svc, IHttpContextAccessor ctx)
    {
        var buyerId = ctx.HttpContext!.User.FindFirstValue(ClaimTypes.Email)
            ?? ctx.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.HttpContext.User.Identity?.Name
            ?? throw new UnauthorizedAccessException();

        var results = await svc.GetShopperOrdersAsync(buyerId);
        var dtos = results.Select(r => new MyOrderDto(
            r.Order.Id,
            r.Order.Status.ToString(),
            r.Payment?.PayPalOrderId,
            r.Payment?.AuthorizationId,
            r.Payment?.CaptureId)).ToList();

        return Results.Ok(new MyOrdersResponse(dtos));
    }
}
