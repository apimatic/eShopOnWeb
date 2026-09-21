using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
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

public class MyOrdersRequest : BaseRequest
{
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken Ct { get; set; }
}

/// <summary>The caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IPaymentService service, CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.CurrentBuyerId(http);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(new MyOrdersRequest { BuyerId = buyerId, Ct = ct }, service);
            })
            .Produces<List<OrderPaymentView>>()
            .WithTags("PayPalOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IPaymentService service)
    {
        var payments = await service.GetMyOrderPaymentsAsync(request.BuyerId, request.Ct);
        var views = payments
            .OrderByDescending(p => p.CreatedAt)
            .Select(PaymentMapping.ToView)
            .ToList();
        return Results.Ok(views);
    }
}
