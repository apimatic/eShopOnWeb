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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

internal static class PaymentEndpointHelpers
{
    public const string Tag = "PaymentEndpoints";

    public static string? BuyerId(ClaimsPrincipal user) => user.Identity?.Name;

    /// <summary>Build the ship-to address from the (optional) request, filling required fields with placeholders.</summary>
    public static Address ToAddress(ShipToAddressDto? dto) => new(
        street: string.IsNullOrWhiteSpace(dto?.Street) ? "N/A" : dto!.Street!,
        city: string.IsNullOrWhiteSpace(dto?.City) ? "N/A" : dto!.City!,
        state: dto?.State ?? string.Empty,
        country: string.IsNullOrWhiteSpace(dto?.Country) ? "US" : dto!.Country!,
        zipcode: string.IsNullOrWhiteSpace(dto?.ZipCode) ? "00000" : dto!.ZipCode!);
}

/// <summary>POST /api/orders — place an order from catalog items; it starts awaiting payment.</summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderEndpoint.Command, IOrderPaymentService>
{
    public record Command(string BuyerId, PlaceOrderRequest Body, CancellationToken CancellationToken);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, HttpContext http) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(new Command(buyerId, request, http.RequestAborted), service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags(PaymentEndpointHelpers.Tag);
    }

    public async Task<IResult> HandleAsync(Command command, IOrderPaymentService service)
    {
        var lines = (command.Body.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var address = PaymentEndpointHelpers.ToAddress(command.Body.ShipToAddress);

        var orderId = await service.PlaceOrderAsync(command.BuyerId, lines, address, command.CancellationToken);
        var payment = await service.GetPaymentAsync(orderId, command.CancellationToken);

        var response = new PlaceOrderResponse
        {
            OrderId = orderId,
            Status = payment?.Status.ToString() ?? "AwaitingPayment",
            Amount = payment?.Amount ?? 0m,
            Currency = payment?.Currency ?? string.Empty
        };
        return Results.Created($"api/orders/{orderId}", response);
    }
}

/// <summary>POST /api/orders/{orderId}/pay — authorize (hold) the order total with a card or saved card.</summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderEndpoint.Command, IOrderPaymentService>
{
    public record Command(string BuyerId, int OrderId, PayOrderRequest Body, CancellationToken CancellationToken);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, HttpContext http) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(new Command(buyerId, orderId, request, http.RequestAborted), service);
            })
            .Produces<OrderPaymentDto>()
            .WithTags(PaymentEndpointHelpers.Tag);
    }

    public async Task<IResult> HandleAsync(Command command, IOrderPaymentService service)
    {
        var instruction = new PayInstruction
        {
            Card = command.Body.Card?.ToCardDetails(),
            SavedPaymentMethodId = command.Body.PaymentMethodId
        };
        var payment = await service.PayAsync(command.BuyerId, command.OrderId, instruction, command.CancellationToken);
        return Results.Ok(OrderPaymentDto.From(payment));
    }
}

/// <summary>POST /api/orders/{orderId}/fulfil — operator captures the held funds.</summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderEndpoint.Command, IOrderPaymentService>
{
    public record Command(int OrderId, CancellationToken CancellationToken);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, HttpContext http) =>
                await HandleAsync(new Command(orderId, http.RequestAborted), service))
            .Produces<OrderPaymentDto>()
            .WithTags(PaymentEndpointHelpers.Tag);
    }

    public async Task<IResult> HandleAsync(Command command, IOrderPaymentService service)
    {
        var payment = await service.FulfilAsync(command.OrderId, command.CancellationToken);
        return Results.Ok(OrderPaymentDto.From(payment));
    }
}

/// <summary>POST /api/orders/{orderId}/cancel — operator cancels before fulfilment, releasing held funds.</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderEndpoint.Command, IOrderPaymentService>
{
    public record Command(int OrderId, CancellationToken CancellationToken);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, HttpContext http) =>
                await HandleAsync(new Command(orderId, http.RequestAborted), service))
            .Produces<OrderPaymentDto>()
            .WithTags(PaymentEndpointHelpers.Tag);
    }

    public async Task<IResult> HandleAsync(Command command, IOrderPaymentService service)
    {
        var payment = await service.CancelAsync(command.OrderId, command.CancellationToken);
        return Results.Ok(OrderPaymentDto.From(payment));
    }
}

/// <summary>POST /api/orders/{orderId}/refunds — refund the caller's captured order, full or partial.</summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderEndpoint.Command, IOrderPaymentService>
{
    public record Command(string BuyerId, int OrderId, RefundOrderRequest Body, CancellationToken CancellationToken);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, HttpContext http) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(new Command(buyerId, orderId, request, http.RequestAborted), service);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags(PaymentEndpointHelpers.Tag);
    }

    public async Task<IResult> HandleAsync(Command command, IOrderPaymentService service)
    {
        if (string.IsNullOrWhiteSpace(command.Body.IdempotencyKey))
            return Results.BadRequest(new { message = "An idempotencyKey is required for a refund." });

        var refund = await service.RefundAsync(command.BuyerId, command.OrderId, command.Body.Amount,
            command.Body.IdempotencyKey, command.CancellationToken);
        var payment = await service.GetPaymentAsync(command.OrderId, command.CancellationToken);

        var response = new RefundResponse
        {
            RefundId = refund.RefundId,
            OrderId = command.OrderId,
            Amount = refund.Amount,
            Status = refund.Status,
            RefundableRemaining = payment?.RefundableRemaining ?? 0m,
            PaymentStatus = payment?.Status.ToString() ?? string.Empty
        };
        return Results.Created($"api/orders/{command.OrderId}/refunds/{refund.RefundId}", response);
    }
}

/// <summary>GET /api/my-orders — the caller's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersEndpoint.Command, IOrderPaymentService>
{
    public record Command(string BuyerId, CancellationToken CancellationToken);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service, HttpContext http) =>
            {
                var buyerId = PaymentEndpointHelpers.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(new Command(buyerId, http.RequestAborted), service);
            })
            .Produces<List<OrderPaymentDto>>()
            .WithTags(PaymentEndpointHelpers.Tag);
    }

    public async Task<IResult> HandleAsync(Command command, IOrderPaymentService service)
    {
        var payments = await service.GetMyPaymentsAsync(command.BuyerId, command.CancellationToken);
        return Results.Ok(payments.Select(OrderPaymentDto.From).ToList());
    }
}

/// <summary>GET /api/reconciliation?from=&to= — operator report lining PayPal transactions up with eShop orders.</summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationEndpoint.Command, IOrderPaymentService>
{
    public record Command(DateTimeOffset From, DateTimeOffset To, CancellationToken CancellationToken);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService service, HttpContext http) =>
                await HandleAsync(new Command(from, to, http.RequestAborted), service))
            .Produces<ReconciliationReport>()
            .WithTags(PaymentEndpointHelpers.Tag);
    }

    public async Task<IResult> HandleAsync(Command command, IOrderPaymentService service)
    {
        var report = await service.ReconcileAsync(command.From, command.To, command.CancellationToken);
        return Results.Ok(report);
    }
}
