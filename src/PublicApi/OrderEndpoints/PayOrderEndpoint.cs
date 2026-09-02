using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total on a card — either one-off card details or one of the
/// shopper's saved cards. No money moves until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal>
{
    private readonly OrderPaymentService _paymentService;

    public PayOrderEndpoint(OrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await Handle(request, user, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user)
        => Handle(request, user, CancellationToken.None);

    private async Task<IResult> Handle(PayOrderRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        try
        {
            var buyerId = user.Identity?.Name;
            if (buyerId is null)
            {
                return Results.Unauthorized();
            }

            GatewayCard? card = request.Card is null ? null : new GatewayCard
            {
                Number = request.Card.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.Cvc,
                Name = request.Card.Name,
                AddressLine1 = request.Card.AddressLine1,
                City = request.Card.City,
                State = request.Card.State,
                PostalCode = request.Card.PostalCode,
                CountryCode = request.Card.CountryCode
            };

            var (order, payment, _) = await _paymentService.PayAsync(
                buyerId, request.OrderId, card, request.PaymentMethodId, ct);

            return Results.Ok(new PayOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                AuthorizationId = payment.AuthorizationId,
                AuthorizationStatus = payment.AuthorizationStatus,
                Amount = order.Total(),
                Currency = payment.Currency,
                PaymentMethod = payment.PaymentMethodDescription
            });
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or PaymentGatewayException)
        {
            return ApiErrorResults.FromException(ex);
        }
    }
}
