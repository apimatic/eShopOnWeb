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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class MyOrdersRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public MyOrdersRequest(string buyerId) => BuyerId = buyerId;
}

public class MyOrdersResponse
{
    public IReadOnlyList<OrderSummaryView> Orders { get; set; } = new List<OrderSummaryView>();
}

/// <summary>The caller's own orders, each with its payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new MyOrdersRequest(user.BuyerId()), service, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        var orders = await service.GetMyOrdersAsync(request.BuyerId, ct);
        return Results.Ok(new MyOrdersResponse { Orders = orders });
    }
}
