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

/// <summary>
/// Shopper action. Lists the caller's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, EmptyRequest, IPaymentReadService>
{
    private readonly IHttpContextAccessor _http;

    public MyOrdersEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentReadService readService) =>
                await HandleAsync(new EmptyRequest(), readService))
            .Produces<MyOrderDto[]>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest _, IPaymentReadService readService)
    {
        var buyerId = EndpointCaller.RequireBuyerId(_http);
        var views = await readService.GetMyOrdersAsync(buyerId);

        var response = views.Select(v => new MyOrderDto
        {
            OrderId = v.Order.Id,
            OrderDate = v.Order.OrderDate,
            Status = v.Order.Status.ToString(),
            Total = v.Order.Total(),
            Items = PaymentMapping.ToLineDtos(v.Order),
            Payment = v.Payment is null ? null : PaymentMapping.ToPaymentResponse(v.Payment, v.Order.Status)
        }).ToList();

        return Results.Ok(response);
    }
}
