using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IRepository<Order>>
{
    private readonly IPayPalService _paypal;

    public FulfilOrderEndpoint(IPayPalService paypal) => _paypal = paypal;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepo) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, orderRepo);
            })
            .Produces<FulfilOrderResponse>()
            .Produces(400)
            .Produces(404)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IRepository<Order> orderRepo)
    {
        var spec = new OrderWithRefundsSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec);
        if (order == null)
            return Results.NotFound();

        // Idempotency: already fulfilled
        if (order.Status == OrderStatus.Fulfilled)
        {
            return Results.Ok(new FulfilOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                CaptureId = order.PayPalCaptureId,
                CapturedAmount = order.CapturedAmount,
                PayPalFee = order.PayPalFee,
                NetAmount = order.NetAmount,
                Status = order.Status.ToString()
            });
        }

        if (order.Status != OrderStatus.PaymentAuthorized)
            return Results.BadRequest(new { error = $"Cannot fulfil order in status {order.Status}." });

        if (string.IsNullOrEmpty(order.PayPalAuthorizationId))
            return Results.BadRequest(new { error = "Order has no PayPal authorization on record." });

        try
        {
            // Check authorization staleness
            var authDetails = await _paypal.GetAuthorizationAsync(order.PayPalAuthorizationId, CancellationToken.None);
            string authId = order.PayPalAuthorizationId;

            if (authDetails.Status == "VOIDED")
                return Results.BadRequest(new { error = "Authorization has been voided and cannot be captured." });

            if (!string.IsNullOrEmpty(authDetails.ExpirationTime) &&
                DateTimeOffset.TryParse(authDetails.ExpirationTime, out var expiresAt) &&
                DateTimeOffset.UtcNow > expiresAt)
            {
                // Authorization is expired — check if within reauth window (days 4-29 from creation)
                var canReauth = false;
                if (!string.IsNullOrEmpty(authDetails.CreateTime) &&
                    DateTimeOffset.TryParse(authDetails.CreateTime, out var createdAt))
                {
                    var ageInDays = (DateTimeOffset.UtcNow - createdAt).TotalDays;
                    if (ageInDays >= 30)
                    {
                        return Results.UnprocessableEntity(new
                        {
                            error = "Authorization expired beyond the reauthorizable window (30+ days). Customer must place a new order."
                        });
                    }
                    canReauth = true;
                }
                else
                {
                    canReauth = true; // Assume reauth possible if no creation time available
                }

                if (canReauth)
                {
                    try
                    {
                        var reauth = await _paypal.ReauthorizeAsync(
                            order.PayPalAuthorizationId, order.Total(), CancellationToken.None);
                        order.UpdateAuthorization(reauth.AuthorizationId, reauth.Status);
                        authId = reauth.AuthorizationId;
                    }
                    catch (PayPalException reauthEx)
                    {
                        return Results.UnprocessableEntity(new
                        {
                            error = $"Authorization expired and reauthorization failed: {reauthEx.Message}. Customer may need to reauthorize payment."
                        });
                    }
                }
            }

            var capture = await _paypal.CaptureAsync(authId, CancellationToken.None);
            order.Fulfill(capture.CaptureId, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
            await orderRepo.UpdateAsync(order);

            return Results.Ok(new FulfilOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                CaptureId = capture.CaptureId,
                CapturedAmount = capture.CapturedAmount,
                PayPalFee = capture.PayPalFee,
                NetAmount = capture.NetAmount,
                Status = order.Status.ToString()
            });
        }
        catch (PayPalException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
    public string? CaptureId { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
    public string? Status { get; set; }
}
