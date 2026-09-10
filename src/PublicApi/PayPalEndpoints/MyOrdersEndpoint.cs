using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

/// <summary>The caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = CallerIdentity.BuyerId(http), Ct = ct }, service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("PayPalPayments");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService service)
    {
        var views = await service.GetMyOrdersAsync(request.BuyerId, request.Ct);
        var response = new MyOrdersResponse(request.CorrelationId())
        {
            Orders = views.Select(v => PaymentMapper.ToResponse(v, request.CorrelationId())).ToList(),
        };
        return Results.Ok(response);
    }
}
