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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>
/// GET /api/my-orders — the caller's orders with their payment state. Shopper-scoped.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService paymentService) =>
                await HandleAsync(paymentService))
            .Produces<MyOrdersResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var orders = await paymentService.GetOrdersForBuyerAsync(buyerId);
        var response = new MyOrdersResponse(Guid.NewGuid())
        {
            Orders = orders.Select(o => PaymentMapper.ToSummaryDto(o.Order, o.Payment)).ToList()
        };
        return Results.Ok(response);
    }
}
