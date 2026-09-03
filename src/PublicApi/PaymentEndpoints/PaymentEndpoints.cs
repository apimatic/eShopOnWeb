using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PaymentEndpoints : IEndpoint
{
    private const string Tag = "Payments";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (PlaceOrderRequest request, HttpContext context, IPaymentService payments) =>
                    await Handle(async () =>
                    {
                        ValidateShipping(request.ShippingAddress);
                        var order = await payments.PlaceOrderAsync(UserName(context.User),
                            request.Items.Select(item => new CatalogItemQuantity(item.CatalogItemId, item.Quantity)).ToList(),
                            new ShippingAddressInput(request.ShippingAddress.Street, request.ShippingAddress.City,
                                request.ShippingAddress.State, request.ShippingAddress.Country,
                                request.ShippingAddress.PostalCode),
                            context.RequestAborted);
                        return Results.Created($"/api/orders/{order.OrderId}", new { orderId = order.OrderId, order });
                    }))
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags(Tag);

        app.MapPost("api/orders/{orderId:int}/pay",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (int orderId, PayOrderRequest request, HttpContext context, IPaymentService payments) =>
                    await Handle(async () =>
                    {
                        var card = request.Card is null ? null : ToCard(request.Card);
                        var order = await payments.PayAsync(orderId, UserName(context.User),
                            new PayOrderInput(card, request.PaymentMethodId), context.RequestAborted);
                        return Results.Ok(order);
                    }))
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags(Tag);

        app.MapPost("api/orders/{orderId:int}/fulfil",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (int orderId, HttpContext context, IPaymentService payments) =>
                    await Handle(async () => Results.Ok(await payments.FulfilAsync(orderId,
                        context.RequestAborted))))
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags(Tag);

        app.MapPost("api/orders/{orderId:int}/cancel",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (int orderId, HttpContext context, IPaymentService payments) =>
                    await Handle(async () => Results.Ok(await payments.CancelAsync(orderId,
                        context.RequestAborted))))
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags(Tag);

        app.MapPost("api/orders/{orderId:int}/refunds",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (int orderId, RefundOrderRequest request, HttpContext context, IPaymentService payments) =>
                    await Handle(async () =>
                    {
                        var refund = await payments.RefundAsync(orderId, UserName(context.User),
                            new RefundInput(request.Amount, request.IdempotencyKey), context.RequestAborted);
                        return Results.Created($"/api/orders/{orderId}/refunds/{refund.RefundId}",
                            new { refundId = refund.RefundId, order = refund.Order });
                    }))
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags(Tag);

        app.MapGet("api/my-orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (HttpContext context, IPaymentService payments) =>
                    await Handle(async () => Results.Ok(await payments.GetOrdersAsync(UserName(context.User),
                        context.RequestAborted))))
            .Produces(StatusCodes.Status200OK)
            .WithTags(Tag);

        app.MapPost("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (SavePaymentMethodRequest request, HttpContext context, IPaymentService payments) =>
                    await Handle(async () =>
                    {
                        var method = await payments.SavePaymentMethodAsync(UserName(context.User),
                            ToCard(request.Card), context.RequestAborted);
                        return Results.Created($"/api/payment-methods/{method.PaymentMethodId}",
                            new { paymentMethodId = method.PaymentMethodId, paymentMethod = method });
                    }))
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags(Tag);

        app.MapGet("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (HttpContext context, IPaymentService payments) =>
                    await Handle(async () => Results.Ok(await payments.GetPaymentMethodsAsync(UserName(context.User),
                        context.RequestAborted))))
            .Produces(StatusCodes.Status200OK)
            .WithTags(Tag);

        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (int paymentMethodId, HttpContext context, IPaymentService payments) =>
                    await Handle(async () =>
                    {
                        await payments.DeletePaymentMethodAsync(paymentMethodId, UserName(context.User),
                            context.RequestAborted);
                        return Results.NoContent();
                    }))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags(Tag);

        app.MapGet("api/reconciliation",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (DateTimeOffset from, DateTimeOffset to, HttpContext context, IPaymentService payments) =>
                    await Handle(async () => Results.Ok(await payments.ReconcileAsync(from, to,
                        context.RequestAborted))))
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags(Tag);
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PaymentException exception)
        {
            var status = exception.Kind switch
            {
                PaymentFailureKind.Validation => StatusCodes.Status400BadRequest,
                PaymentFailureKind.NotFound => StatusCodes.Status404NotFound,
                PaymentFailureKind.Conflict or PaymentFailureKind.PayerActionRequired => StatusCodes.Status409Conflict,
                PaymentFailureKind.ProviderRejected => StatusCodes.Status422UnprocessableEntity,
                PaymentFailureKind.ProviderUnavailable => StatusCodes.Status502BadGateway,
                PaymentFailureKind.UnknownOutcome => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status500InternalServerError
            };
            var body = new Dictionary<string, object?>
            {
                ["error"] = exception.Message,
                ["providerDebugId"] = exception.ProviderDebugId
            };
            return Results.Json(body, statusCode: status);
        }
    }

    private static string UserName(ClaimsPrincipal user) => user.Identity?.Name ??
        throw new PaymentException(PaymentFailureKind.Validation, "The JWT does not identify a shopper.");

    private static CardInput ToCard(CardRequest card)
    {
        if (string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine1) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.City) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.PostalCode) ||
            card.BillingAddress.CountryCode.Length != 2)
        {
            throw new PaymentException(PaymentFailureKind.Validation, "Complete card and billing address details are required.");
        }

        var digits = new string(card.Number.Where(char.IsDigit).ToArray());
        var expiryIsValid = DateOnly.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd", out var expiryMonth) &&
                            expiryMonth >= new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        if (digits.Length is < 13 or > 19 || card.SecurityCode.Length is < 3 or > 4 ||
            card.SecurityCode.Any(character => !char.IsDigit(character)) || !expiryIsValid)
        {
            throw new PaymentException(PaymentFailureKind.Validation, "Card number, expiry, or security code format is invalid.");
        }

        return new CardInput(card.Name, digits, card.Expiry, card.SecurityCode,
            new BillingAddressInput(card.BillingAddress.AddressLine1, card.BillingAddress.AddressLine2,
                card.BillingAddress.City, card.BillingAddress.State, card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode.ToUpperInvariant()));
    }

    private static void ValidateShipping(ShippingAddressRequest address)
    {
        if (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.PostalCode))
        {
            throw new PaymentException(PaymentFailureKind.Validation, "A complete shipping address is required.");
        }
    }
}
