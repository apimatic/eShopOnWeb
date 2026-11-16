using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Finix;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }
}

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest>
{
    private readonly CatalogContext _db;
    private readonly FinixClient _finix;
    private readonly IOptions<FinixOptions> _finixOptions;

    public CancelOrderEndpoint(CatalogContext db, FinixClient finix, IOptions<FinixOptions> finixOptions)
    {
        _db = db;
        _finix = finix;
        _finixOptions = finixOptions;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int orderId) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId });
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request)
    {
        var cancellationToken = CancellationToken.None;
        var orderLock = PaymentHelpers.LockForOrder(request.OrderId);
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await _db.Orders
                .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
                .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

            if (order is null)
            {
                return Results.NotFound();
            }

            var payment = await _db.Payments
                .FirstOrDefaultAsync(p => p.OrderId == order.Id, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return Results.Ok(await BuildResponseAsync(order, payment, cancellationToken));
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "A fulfilled order cannot be cancelled.",
                    detail: "The payment has already been captured. Use the refunds endpoint instead.");
            }

            if (payment?.FinixAuthorizationId is not null && !payment.AuthorizationVoided)
            {
                try
                {
                    var voided = await _finix.VoidAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
                    payment.Void(voided.State, voided.VoidState);
                }
                catch (FinixApiException exception)
                {
                    FinixAuthorization? refreshed = null;
                    try
                    {
                        refreshed = await _finix.GetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
                        payment.UpdateAuthorizationState(refreshed.State, refreshed.VoidState, refreshed.IsVoid);
                    }
                    catch (FinixApiException)
                    {
                    }

                    if (refreshed is null || (!refreshed.IsVoid && refreshed.State != "CANCELED"))
                    {
                        await _db.SaveChangesAsync(cancellationToken);
                        return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                            title: "The authorization hold could not be released.",
                            detail: $"Finix reports the hold is not voided (state: {refreshed?.State ?? "UNKNOWN"}, " +
                                    $"void_state: {refreshed?.VoidState ?? "UNKNOWN"}). " +
                                    $"If the payment was captured, use refunds instead. Finix said: {exception.Message}");
                    }
                }
            }

            order.MarkCancelled();
            await _db.SaveChangesAsync(cancellationToken);

            return Results.Ok(await BuildResponseAsync(order, payment, cancellationToken));
        }
        finally
        {
            orderLock.Release();
        }
    }

    private async Task<OrderActionResponse> BuildResponseAsync(Order order, Payment? payment,
        CancellationToken cancellationToken)
    {
        var refunds = payment is null
            ? new List<PaymentRefund>()
            : await _db.PaymentRefunds.Where(r => r.PaymentId == payment.Id).ToListAsync(cancellationToken);

        return new OrderActionResponse(Guid.NewGuid())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Payment = PaymentMapping.ToDto(order, payment, refunds, _finixOptions.Value.Currency)
        };
    }
}
