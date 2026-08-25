using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, int, IRepository<Order>>
{
    private readonly IPaymentService _paymentService;
    private readonly string _currency;

    public FulfilOrderEndpoint(IPaymentService paymentService, IConfiguration config)
    {
        _paymentService = paymentService;
        _currency = config["PayPal:Currency"] ?? "USD";
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IRepository<Order> orderRepo, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, orderRepo);
            })
            .Produces<FulfilOrderResponse>()
            .ProducesProblem(400)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepo)
    {
        var order = await orderRepo.GetByIdAsync(orderId);
        if (order is null)
            return Results.NotFound();
        if (order.PaymentStatus != OrderPaymentStatus.Authorized)
            return Results.BadRequest($"Order is in '{order.PaymentStatus}' state and cannot be fulfilled.");
        if (string.IsNullOrEmpty(order.AuthorizationId))
            return Results.BadRequest("Order has no authorization ID.");

        var authId = order.AuthorizationId;

        // Re-authorize if the authorization has expired and is within the 29-day window
        var isExpired = order.AuthorizationExpiry.HasValue && order.AuthorizationExpiry.Value < DateTimeOffset.UtcNow;
        if (isExpired)
        {
            if (order.AuthorizationCreatedAt.HasValue &&
                (DateTimeOffset.UtcNow - order.AuthorizationCreatedAt.Value).TotalDays >= 29)
            {
                throw new PaymentException("Authorization expired beyond the re-authorization window. Place a new order.", 422);
            }

            authId = await _paymentService.ReauthorizeAsync(order.AuthorizationId, order.Total(), _currency);
            order.RenewAuthorization(authId, DateTimeOffset.UtcNow.AddDays(29));
            await orderRepo.UpdateAsync(order);
        }

        var capture = await _paymentService.CapturePaymentAsync(authId, order.Total(), _currency, order.Id.ToString());
        order.SetPaymentCaptured(capture.CaptureId, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        await orderRepo.UpdateAsync(order);

        return Results.Ok(new FulfilOrderResponse(Guid.NewGuid())
        {
            CaptureId = capture.CaptureId,
            CapturedAmount = capture.CapturedAmount,
            PayPalFee = capture.PayPalFee,
            NetAmount = capture.NetAmount
        });
    }
}
