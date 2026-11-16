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

public class FulfilOrderRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }
}

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest>
{
    private readonly CatalogContext _db;
    private readonly FinixClient _finix;
    private readonly IOptions<FinixOptions> _finixOptions;

    public FulfilOrderEndpoint(CatalogContext db, FinixClient finix, IOptions<FinixOptions> finixOptions)
    {
        _db = db;
        _finix = finix;
        _finixOptions = finixOptions;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int orderId) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId });
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request)
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

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded &&
                payment?.FinixTransferId is not null)
            {
                return Results.Ok(await BuildResponseAsync(order, payment, cancellationToken));
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The order has been cancelled.",
                    detail: "A cancelled order cannot be fulfilled.");
            }

            if (order.Status != OrderStatus.Authorized || payment?.FinixAuthorizationId is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The order has no authorized payment.",
                    detail: $"The order is currently {order.Status}. Authorize payment before fulfilling it.");
            }

            FinixAuthorization authorization;
            try
            {
                authorization = await _finix.GetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
            }
            catch (FinixApiException exception)
            {
                return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                    title: "Unable to read the authorization from Finix.",
                    detail: exception.Message);
            }

            payment.UpdateAuthorizationState(authorization.State, authorization.VoidState, authorization.IsVoid);

            if (authorization.IsVoid || authorization.State == "CANCELED")
            {
                await _db.SaveChangesAsync(cancellationToken);
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The authorization hold can no longer be captured.",
                    detail: BuildHoldMessage(order, authorization, null));
            }

            if (authorization.State == "FAILED")
            {
                await _db.SaveChangesAsync(cancellationToken);
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The authorization hold can no longer be captured.",
                    detail: BuildHoldMessage(order, authorization, null));
            }

            var capturedAmountCents = payment.AuthorizedAmountCents;
            string? transferId = authorization.Transfer;

            if (string.IsNullOrWhiteSpace(transferId))
            {
                try
                {
                    var captured = await _finix.CaptureAuthorizationAsync(payment.FinixAuthorizationId,
                        payment.AuthorizedAmountCents, cancellationToken);

                    if (captured.State == "FAILED")
                    {
                        payment.RecordFailure(captured.FailureCode, captured.FailureMessage);
                        await _db.SaveChangesAsync(cancellationToken);
                        return Results.Problem(statusCode: StatusCodes.Status402PaymentRequired,
                            title: "The payment capture failed.",
                            detail: captured.FailureMessage ?? "Finix declined the capture.");
                    }

                    if (captured.Amount > 0)
                    {
                        capturedAmountCents = captured.Amount;
                    }

                    transferId = captured.Transfer;
                }
                catch (FinixApiException exception)
                {
                    var refreshed = await TryRefreshAuthorizationAsync(payment, cancellationToken);
                    if (refreshed is not null && !string.IsNullOrWhiteSpace(refreshed.Transfer))
                    {
                        transferId = refreshed.Transfer;
                    }
                    else
                    {
                        await _db.SaveChangesAsync(cancellationToken);
                        return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                            title: "The authorization hold can no longer be captured.",
                            detail: BuildHoldMessage(order, refreshed ?? authorization, exception));
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(transferId))
            {
                await _db.SaveChangesAsync(cancellationToken);
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "The payment was captured but Finix did not return a transfer id.",
                    detail: "Contact Finix support with the authorization id before retrying fulfilment.");
            }

            var captureDetails = await GetCaptureDetailsAsync(transferId, cancellationToken);
            var transfer = captureDetails.Transfer;
            if (transfer.Amount > 0)
            {
                capturedAmountCents = transfer.Amount;
            }

            if (transfer.State == "FAILED")
            {
                payment.RecordFailure(transfer.FailureCode, transfer.FailureMessage);
                await _db.SaveChangesAsync(cancellationToken);
                return Results.Problem(statusCode: StatusCodes.Status402PaymentRequired,
                    title: "The payment capture failed.",
                    detail: transfer.FailureMessage ?? "Finix declined the capture.");
            }

            payment.Capture(transferId, transfer.State, capturedAmountCents, captureDetails.FeeAmountCents);
            order.MarkFulfilled();
            await _db.SaveChangesAsync(cancellationToken);

            return Results.Ok(await BuildResponseAsync(order, payment, cancellationToken));
        }
        finally
        {
            orderLock.Release();
        }
    }

    private async Task<FinixAuthorization?> TryRefreshAuthorizationAsync(Payment payment,
        CancellationToken cancellationToken)
    {
        try
        {
            var refreshed = await _finix.GetAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
            payment.UpdateAuthorizationState(refreshed.State, refreshed.VoidState, refreshed.IsVoid);
            return refreshed;
        }
        catch (FinixApiException)
        {
            return null;
        }
    }

    private async Task<(FinixTransfer Transfer, decimal FeeAmountCents)> GetCaptureDetailsAsync(string transferId,
        CancellationToken cancellationToken)
    {
        FinixTransfer? transfer = null;

        for (var attempt = 0; attempt < 120; attempt++)
        {
            try
            {
                transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
            }
            catch (FinixApiException exception) when (exception.StatusCode == StatusCodes.Status404NotFound &&
                                                      attempt < 119)
            {
                await Task.Delay(1000, cancellationToken);
                continue;
            }

            if (transfer.State == "FAILED")
            {
                return (transfer, 0m);
            }

            var fees = await _finix.ListFeesForTransferAsync(transferId, cancellationToken);
            if (fees.Count > 0 && transfer.State != "PENDING")
            {
                return (transfer, fees.Sum(f => f.Amount));
            }

            if (attempt < 119)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }

        transfer ??= await _finix.GetTransferAsync(transferId, cancellationToken);
        var finalFees = await _finix.ListFeesForTransferAsync(transferId, cancellationToken);
        return (transfer, finalFees.Sum(f => f.Amount));
    }

    private static string BuildHoldMessage(Order order, FinixAuthorization? authorization,
        FinixApiException? exception)
    {
        var state = authorization?.State ?? "UNKNOWN";
        var voidState = authorization?.VoidState ?? "UNKNOWN";
        var message = $"Finix reports the authorization hold is no longer capturable (state: {state}, " +
                      $"void_state: {voidState}). The hold may have expired or been voided. " +
                      $"The order is still {order.Status}; cancel it or ask the shopper to authorize payment again.";

        if (exception is not null)
        {
            message += $" Finix said: {exception.Message}";
        }

        return message;
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
