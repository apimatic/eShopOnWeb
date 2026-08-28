using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class CreateOrderRequest
{
    public IReadOnlyCollection<OrderLineInput> Items { get; set; } = Array.Empty<OrderLineInput>();
    public ShippingAddressInput ShippingAddress { get; set; } = null!;
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public sealed record CreateOrderResponse(int OrderId);

public sealed class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ClaimsPrincipal user, IPaymentApplicationService service,
                CancellationToken cancellationToken) =>
            {
                request.BuyerId = Caller(user);
                return await HandleAsync(request, service, cancellationToken);
            }).Produces<CreateOrderResponse>(StatusCodes.Status201Created).WithTags("PaymentEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentApplicationService service) =>
        HandleAsync(request, service, CancellationToken.None);

    private static async Task<IResult> HandleAsync(CreateOrderRequest request,
        IPaymentApplicationService service, CancellationToken cancellationToken)
    {
        var id = await service.CreateOrderAsync(request.BuyerId, request.Items, request.ShippingAddress,
            cancellationToken);
        return Results.Created($"/api/orders/{id}", new CreateOrderResponse(id));
    }

    internal static string Caller(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
}

public sealed class PayOrderRequest
{
    public CardInput? Card { get; set; }
    public int? PaymentMethodId { get; set; }
    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public sealed class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ClaimsPrincipal user,
                IPaymentApplicationService service, CancellationToken cancellationToken) =>
            {
                request.OrderId = orderId;
                request.BuyerId = CreateOrderEndpoint.Caller(user);
                return Results.Ok(await service.PayAsync(request.BuyerId, orderId, request.Card,
                    request.PaymentMethodId, cancellationToken));
            }).Produces<PaymentResult>().WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentApplicationService service) =>
        Results.Ok(await service.PayAsync(request.BuyerId, request.OrderId, request.Card,
            request.PaymentMethodId, CancellationToken.None));
}

public sealed record OrderActionRequest(int OrderId);

public sealed class FulfilOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentApplicationService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.FulfilAsync(orderId, cancellationToken)))
            .Produces<PaymentResult>().WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IPaymentApplicationService service) =>
        Results.Ok(await service.FulfilAsync(request.OrderId, CancellationToken.None));
}

public sealed class CancelOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentApplicationService service, CancellationToken cancellationToken) =>
                Results.Ok(await service.CancelAsync(orderId, cancellationToken)))
            .Produces<PaymentResult>().WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IPaymentApplicationService service) =>
        Results.Ok(await service.CancelAsync(request.OrderId, CancellationToken.None));
}

public sealed class RefundOrderRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public sealed class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, ClaimsPrincipal user,
                IPaymentApplicationService service, CancellationToken cancellationToken) =>
            {
                request.OrderId = orderId;
                request.BuyerId = CreateOrderEndpoint.Caller(user);
                return Results.Ok(await service.RefundAsync(request.BuyerId, orderId,
                    request.IdempotencyKey, request.Amount, cancellationToken));
            }).Produces<RefundResult>().WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentApplicationService service) =>
        Results.Ok(await service.RefundAsync(request.BuyerId, request.OrderId, request.IdempotencyKey,
            request.Amount, CancellationToken.None));
}

public sealed record GetMyOrdersRequest(string BuyerId);
public sealed record GetMyOrdersResponse(IReadOnlyCollection<OrderResult> Orders);

public sealed class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IPaymentApplicationService service,
                CancellationToken cancellationToken) => Results.Ok(new GetMyOrdersResponse(
                await service.GetMyOrdersAsync(CreateOrderEndpoint.Caller(user), cancellationToken))))
            .Produces<GetMyOrdersResponse>().WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, IPaymentApplicationService service) =>
        Results.Ok(new GetMyOrdersResponse(await service.GetMyOrdersAsync(request.BuyerId,
            CancellationToken.None)));
}

public sealed class SavePaymentMethodRequest
{
    public CardInput Card { get; set; } = null!;
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public sealed class SavePaymentMethodEndpoint :
    IEndpoint<IResult, SavePaymentMethodRequest, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, ClaimsPrincipal user,
                IPaymentApplicationService service, CancellationToken cancellationToken) =>
            {
                request.BuyerId = CreateOrderEndpoint.Caller(user);
                var result = await service.SavePaymentMethodAsync(request.BuyerId, request.Card,
                    cancellationToken);
                return Results.Created($"/api/payment-methods/{result.PaymentMethodId}", result);
            }).Produces<PaymentMethodResult>(StatusCodes.Status201Created).WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request,
        IPaymentApplicationService service)
    {
        var result = await service.SavePaymentMethodAsync(request.BuyerId, request.Card,
            CancellationToken.None);
        return Results.Created($"/api/payment-methods/{result.PaymentMethodId}", result);
    }
}

public sealed record GetPaymentMethodsRequest(string BuyerId);
public sealed record GetPaymentMethodsResponse(IReadOnlyCollection<PaymentMethodResult> PaymentMethods);

public sealed class GetPaymentMethodsEndpoint :
    IEndpoint<IResult, GetPaymentMethodsRequest, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IPaymentApplicationService service,
                CancellationToken cancellationToken) => Results.Ok(new GetPaymentMethodsResponse(
                await service.GetPaymentMethodsAsync(CreateOrderEndpoint.Caller(user), cancellationToken))))
            .Produces<GetPaymentMethodsResponse>().WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(GetPaymentMethodsRequest request,
        IPaymentApplicationService service) => Results.Ok(new GetPaymentMethodsResponse(
        await service.GetPaymentMethodsAsync(request.BuyerId, CancellationToken.None)));
}

public sealed record DeletePaymentMethodRequest(int PaymentMethodId, string BuyerId);

public sealed class DeletePaymentMethodEndpoint :
    IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ClaimsPrincipal user, IPaymentApplicationService service,
                CancellationToken cancellationToken) =>
            {
                await service.DeletePaymentMethodAsync(CreateOrderEndpoint.Caller(user), paymentMethodId,
                    cancellationToken);
                return Results.NoContent();
            }).Produces(StatusCodes.Status204NoContent).WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request,
        IPaymentApplicationService service)
    {
        await service.DeletePaymentMethodAsync(request.BuyerId, request.PaymentMethodId,
            CancellationToken.None);
        return Results.NoContent();
    }
}

public sealed record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);

public sealed class ReconciliationEndpoint :
    IEndpoint<IResult, ReconciliationRequest, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IPaymentApplicationService service,
                CancellationToken cancellationToken) =>
                Results.Ok(await service.ReconcileAsync(from, to, cancellationToken)))
            .Produces<ReconciliationResult>().WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request,
        IPaymentApplicationService service) => Results.Ok(await service.ReconcileAsync(request.From,
        request.To, CancellationToken.None));
}
