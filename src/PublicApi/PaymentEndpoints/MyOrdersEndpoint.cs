using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;
using static Microsoft.eShopWeb.PublicApi.PaymentEndpoints.PaymentApiHelpers;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/my-orders — the caller's own orders, each with its payment state. Shopper-scoped.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest>
{
    private readonly IPaymentService _paymentService;

    public MyOrdersEndpoint(IPaymentService paymentService) => _paymentService = paymentService;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) => await HandleAsync(new MyOrdersRequest(user.GetUserName() ?? string.Empty)))
            .Produces<IReadOnlyList<OrderSummaryView>>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request)
    {
        var result = await _paymentService.GetMyOrdersAsync(request.BuyerId);
        return ToHttp(result, orders => Results.Ok(orders));
    }
}
