using System.Collections.Generic;
using System.Linq;
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
}

public class MyOrdersResponse
{
    public List<OrderPaymentDto> Orders { get; set; } = new();
}

/// <summary>The caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = PaymentMappers.BuyerId(user) }, service, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request, IPaymentService service)
        => HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IPaymentService service, CancellationToken ct)
    {
        var payments = await service.GetMyOrdersAsync(request.BuyerId, ct);
        return Results.Ok(new MyOrdersResponse
        {
            Orders = payments.Select(OrderPaymentDto.From).ToList()
        });
    }
}
