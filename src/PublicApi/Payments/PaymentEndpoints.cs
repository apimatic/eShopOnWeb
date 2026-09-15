using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentEndpoints : IEndpoint<IResult, PaymentApplicationService>
{
    public Task<IResult> HandleAsync(PaymentApplicationService service) =>
        Task.FromResult(Results.NotFound());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, PaymentApplicationService service, CancellationToken ct) =>
            {
                var response = await service.PlaceOrderAsync(UserName(user), request, ct);
                return Results.Created($"/api/orders/{response.OrderId}", response);
            }).Produces<OrderCreatedResponse>(StatusCodes.Status201Created).WithTags("Payments");

        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, PaymentApplicationService service, CancellationToken ct) =>
                Results.Ok(await service.PayAsync(UserName(user), orderId, request, ct)))
            .Produces<PaymentResponse>().WithTags("Payments");

        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentApplicationService service, CancellationToken ct) =>
                Results.Ok(await service.FulfilAsync(orderId, ct)))
            .Produces<FulfilmentResponse>().WithTags("Payments");

        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentApplicationService service, CancellationToken ct) =>
                Results.Ok(await service.CancelAsync(orderId, ct)))
            .Produces<CancelResponse>().WithTags("Payments");

        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, PaymentApplicationService service, CancellationToken ct) =>
            {
                var response = await service.RefundAsync(UserName(user), orderId, request, ct);
                return Results.Created($"/api/orders/{orderId}/refunds/{response.RefundId}", response);
            }).Produces<RefundCreatedResponse>(StatusCodes.Status201Created).WithTags("Payments");

        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PaymentApplicationService service, CancellationToken ct) =>
                Results.Ok(await service.MyOrdersAsync(UserName(user), ct)))
            .Produces<MyOrdersResponse>().WithTags("Payments");

        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, PaymentApplicationService service, CancellationToken ct) =>
            {
                var response = await service.SavePaymentMethodAsync(UserName(user), request, ct);
                return Results.Created($"/api/payment-methods/{response.PaymentMethodId}", response);
            }).Produces<PaymentMethodResponse>(StatusCodes.Status201Created).WithTags("Payments");

        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PaymentApplicationService service, CancellationToken ct) =>
                Results.Ok(await service.ListPaymentMethodsAsync(UserName(user), ct)))
            .Produces<PaymentMethodsResponse>().WithTags("Payments");

        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, PaymentApplicationService service, CancellationToken ct) =>
            {
                await service.DeletePaymentMethodAsync(UserName(user), paymentMethodId, ct);
                return Results.NoContent();
            }).Produces(StatusCodes.Status204NoContent).WithTags("Payments");

        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, PaymentApplicationService service, CancellationToken ct) =>
            {
                if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)
                    || !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end))
                    throw new PaymentApiException(StatusCodes.Status400BadRequest, "invalid_range",
                        "from and to must be ISO-8601 date-times.");
                return Results.Ok(await service.ReconcileAsync(start, end, ct));
            }).Produces<ReconciliationResponse>().WithTags("Payments");
    }

    private static string UserName(ClaimsPrincipal user) => user.Identity?.Name ?? string.Empty;
}
