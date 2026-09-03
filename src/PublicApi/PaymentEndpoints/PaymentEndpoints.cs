using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PaymentEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal principal, IPaymentWorkflowService workflow, CancellationToken ct) =>
            {
                if (request.Items is null || request.ShippingAddress is null)
                    throw InvalidRequest("items and shippingAddress are required.");
                var order = await workflow.PlaceOrderAsync(Identity(principal),
                    request.Items.Select(x => new OrderLineInput(x.CatalogItemId, x.Quantity)).ToList(),
                    request.ShippingAddress.ToDomain(), ct);
                return Results.Created($"/api/orders/{order.Id}", new { orderId = order.Id, order = ToOrderResponse(order) });
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("Payments");

        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal principal,
                IPaymentWorkflowService workflow, CancellationToken ct) =>
            {
                var order = await workflow.PayAsync(orderId, Identity(principal), request.Card?.ToInput(),
                    request.PaymentMethodId, ct);
                return Results.Ok(ToOrderResponse(order));
            })
            .WithTags("Payments");

        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentWorkflowService workflow, CancellationToken ct) =>
                Results.Ok(ToOrderResponse(await workflow.FulfilAsync(orderId, ct))))
            .WithTags("Payments");

        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentWorkflowService workflow, CancellationToken ct) =>
                Results.Ok(ToOrderResponse(await workflow.CancelAsync(orderId, ct))))
            .WithTags("Payments");

        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundRequest request, ClaimsPrincipal principal,
                IPaymentWorkflowService workflow, CancellationToken ct) =>
            {
                var refund = await workflow.RefundAsync(orderId, Identity(principal), request.Amount,
                    request.IdempotencyKey, ct);
                return Results.Ok(new
                {
                    refundId = refund.Id,
                    amount = refund.Amount,
                    status = refund.Status.ToString(),
                    payPalRefundId = refund.PayPalRefundId,
                    payPalStatus = refund.PayPalStatus
                });
            })
            .WithTags("Payments");

        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, IPaymentWorkflowService workflow, CancellationToken ct) =>
                Results.Ok((await workflow.GetOrdersAsync(Identity(principal), ct)).Select(ToOrderResponse)))
            .WithTags("Payments");

        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentWorkflowService workflow, CancellationToken ct) =>
                Results.Ok(await workflow.ReconcileAsync(from, to, ct)))
            .WithTags("Payments");

        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal principal,
                IPaymentWorkflowService workflow, CancellationToken ct) =>
            {
                var method = await workflow.SavePaymentMethodAsync(Identity(principal), request.Alias,
                    request.Card?.ToInput() ?? throw InvalidRequest("card is required."), ct);
                return Results.Created($"/api/payment-methods/{method.Id}", ToPaymentMethodResponse(method));
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("Payment methods");

        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal principal, IPaymentWorkflowService workflow, CancellationToken ct) =>
                Results.Ok((await workflow.GetPaymentMethodsAsync(Identity(principal), ct))
                    .Select(ToPaymentMethodResponse)))
            .WithTags("Payment methods");

        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal principal, IPaymentWorkflowService workflow, CancellationToken ct) =>
            {
                await workflow.DeletePaymentMethodAsync(paymentMethodId, Identity(principal), ct);
                return Results.NoContent();
            })
            .WithTags("Payment methods");
    }

    private static string Identity(ClaimsPrincipal principal) =>
        principal.Identity?.Name ?? throw new PaymentWorkflowException(401, "IDENTITY_REQUIRED",
            "The bearer token does not identify a shopper.");

    private static PaymentWorkflowException InvalidRequest(string message) =>
        new(400, "INVALID_REQUEST", message);

    private static object ToPaymentMethodResponse(PaymentMethod method) => new
    {
        paymentMethodId = method.Id,
        alias = method.Alias,
        brand = method.Brand,
        last4 = method.Last4,
        expiry = method.Expiry
    };

    private static object ToOrderResponse(Order order) => new
    {
        orderId = order.Id,
        orderDate = order.OrderDate,
        currency = order.Currency,
        total = order.Total(),
        paymentStatus = order.PaymentStatus.ToString(),
        fulfilmentStatus = order.FulfilmentStatus.ToString(),
        payPalOrderId = order.PayPalOrderId,
        authorizationId = order.AuthorizationId,
        authorizationStatus = order.AuthorizationStatus,
        authorizationExpiresAt = order.AuthorizationExpiresAt,
        captureId = order.CaptureId,
        captureStatus = order.CaptureStatus,
        capturedAmount = order.CapturedAmount,
        payPalFee = order.PayPalFee,
        netProceeds = order.NetProceeds,
        refundedAmount = order.RefundedAmount,
        items = order.OrderItems.Select(x => new
        {
            catalogItemId = x.ItemOrdered.CatalogItemId,
            name = x.ItemOrdered.ProductName,
            unitPrice = x.UnitPrice,
            quantity = x.Units
        }),
        refunds = order.Refunds.Select(x => new
        {
            refundId = x.Id,
            amount = x.Amount,
            status = x.Status.ToString(),
            payPalRefundId = x.PayPalRefundId,
            payPalStatus = x.PayPalStatus,
            createdAt = x.CreatedAt
        })
    };
}

public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineRequest>? Items, ShippingAddressRequest? ShippingAddress);
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode)
{
    public Address ToDomain() => new(Street, City, State, Country, ZipCode);
}

public sealed record PayOrderRequest(CardRequest? Card, int? PaymentMethodId);
public sealed record SavePaymentMethodRequest(string Alias, CardRequest? Card);
public sealed record RefundRequest(decimal? Amount, string IdempotencyKey);

public sealed record CardRequest(string Name, string Number, string Expiry, string SecurityCode,
    CardAddressRequest? BillingAddress)
{
    public CardInput ToInput() => new(Name, Number, Expiry, SecurityCode,
        BillingAddress?.ToInput() ?? throw new PaymentWorkflowException(400, "INVALID_CARD", "billingAddress is required."));
}

public sealed record CardAddressRequest(string AddressLine1, string? AddressLine2, string City, string State,
    string PostalCode, string CountryCode)
{
    public CardAddressInput ToInput() =>
        new(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
}
