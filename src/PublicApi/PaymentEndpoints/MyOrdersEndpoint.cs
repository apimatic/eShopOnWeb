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

/// <summary>The caller's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, OrderOperationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new OrderOperationRequest { BuyerId = user.Identity?.Name, Cancellation = ct }, service);
            })
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderOperationRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var payments = await service.GetOrdersForBuyerAsync(request.BuyerId, request.Cancellation);
        return Results.Ok(payments.Select(OrderPaymentResponse.From).ToList());
    }
}
