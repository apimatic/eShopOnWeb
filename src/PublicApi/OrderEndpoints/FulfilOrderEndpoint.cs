using System;
using System.Linq;
using System.Threading.Tasks;
using BlazorShared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IRepository<Order>>
{
    private readonly IRepository<Payment> _paymentRepository;
    private readonly PayPalClient _payPalClient;
    private readonly PayPalSettings _payPalSettings;

    public FulfilOrderEndpoint(
        IRepository<Payment> paymentRepository,
        PayPalClient payPalClient,
        Microsoft.Extensions.Options.IOptions<PayPalSettings> payPalSettings)
    {
        _paymentRepository = paymentRepository;
        _payPalClient = payPalClient;
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, orderRepository);
            })
            .Produces<FulfilOrderResponse>(200)
            .Produces(400)
            .Produces(404)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IRepository<Order> orderRepository)
    {
        var orderSpec = new OrderByIdWithItemsSpec(request.OrderId);
        var order = await orderRepository.FirstOrDefaultAsync(orderSpec);
        if (order == null)
            return Results.NotFound(new { error = "Order not found." });

        if (order.Status != OrderStatus.PaymentAuthorized)
            return Results.BadRequest(new { error = $"Order status is {order.Status}. Only PaymentAuthorized orders can be fulfilled." });

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(request.OrderId));
        if (payment == null)
            return Results.Problem("Payment record not found for this order.");

        var captureIdempotencyKey = $"fulfil-{request.OrderId}";

        try
        {
            PayPalCaptureResponse capture;
            try
            {
                capture = await _payPalClient.CaptureAuthorizationAsync(
                    payment.AuthorizationId, captureIdempotencyKey);
            }
            catch (PayPalException ex) when (ex.StatusCode == 422 && ex.IsAuthorizationExpired())
            {
                // Authorization stale — attempt reauthorize
                var reauthKey = $"reauth-{request.OrderId}-{Guid.NewGuid():N}";
                try
                {
                    var reauth = await _payPalClient.ReauthorizeAsync(
                        payment.AuthorizationId, payment.AuthorizedAmount,
                        payment.Currency, reauthKey);

                    if (string.IsNullOrEmpty(reauth.Id))
                        return Results.Problem("Reauthorization failed: PayPal did not return a new authorization ID. The order may need to be cancelled and restarted.");

                    payment.UpdateAuthorization(reauth.Id);
                    await _paymentRepository.UpdateAsync(payment);

                    capture = await _payPalClient.CaptureAuthorizationAsync(reauth.Id, captureIdempotencyKey);
                }
                catch (PayPalException reEx)
                {
                    return Results.Problem(
                        detail: $"Authorization is stale and reauthorization failed: {reEx.Message}. The operator should cancel this order and ask the shopper to reorder.",
                        statusCode: 422,
                        title: "ReauthorizationFailed",
                        extensions: reEx.DebugId != null
                            ? new System.Collections.Generic.Dictionary<string, object?> { ["debugId"] = reEx.DebugId }
                            : null);
                }
            }

            decimal? capturedAmount = null;
            decimal? payPalFee = null;
            decimal? netAmount = null;

            if (capture.SellerBreakdown != null)
            {
                if (decimal.TryParse(capture.SellerBreakdown.GrossAmount?.Value, out var gross))
                    capturedAmount = gross;
                if (decimal.TryParse(capture.SellerBreakdown.PayPalFee?.Value, out var fee))
                    payPalFee = fee;
                if (decimal.TryParse(capture.SellerBreakdown.NetAmount?.Value, out var net))
                    netAmount = net;
            }

            payment.RecordCapture(capture.Id, capturedAmount ?? payment.AuthorizedAmount, payPalFee, netAmount);
            await _paymentRepository.UpdateAsync(payment);

            order.SetStatus(OrderStatus.Fulfilled);
            await orderRepository.UpdateAsync(order);

            return Results.Ok(new FulfilOrderResponse
            {
                CaptureId = capture.Id,
                CaptureStatus = capture.Status,
                CapturedAmount = capturedAmount,
                PayPalFee = payPalFee,
                NetAmount = netAmount,
                Currency = payment.Currency
            });
        }
        catch (PayPalException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: ex.StatusCode > 0 ? ex.StatusCode : 502,
                title: "PayPalError",
                extensions: ex.DebugId != null
                    ? new System.Collections.Generic.Dictionary<string, object?> { ["debugId"] = ex.DebugId }
                    : null);
        }
    }
}
