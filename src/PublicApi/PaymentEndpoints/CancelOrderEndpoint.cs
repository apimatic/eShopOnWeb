using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly CatalogContext _db;
    private readonly FinixPaymentService _payments;

    public CancelOrderEndpoint(CatalogContext db, FinixPaymentService payments)
    {
        _db = db;
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
                [Authorize(
                    Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, ClaimsPrincipal user) => await HandleAsync(orderId, user))
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user)
    {
        var order = await OrderQueries.LoadOrderAsync(_db, orderId, CancellationToken.None);
        if (order is null)
        {
            return Results.NotFound();
        }

        var payment = await OrderQueries.LoadPaymentAsync(_db, order.Id, CancellationToken.None);
        if (payment is null)
        {
            return Results.Conflict(new { message = "This order has no payment record." });
        }

        try
        {
            await _payments.CancelAsync(order, payment, CancellationToken.None);
            var refunds = await OrderQueries.LoadRefundsAsync(_db, payment.Id, CancellationToken.None);
            return Results.Ok(PaymentResponseMapper.ToOrderResponse(order, payment, refunds));
        }
        catch (PaymentOperationException exception)
        {
            return PaymentResults.FromException(exception);
        }
    }
}
