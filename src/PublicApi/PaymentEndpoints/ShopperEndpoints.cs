using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Stax;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record PaymentMethodRequest(
    string? Method, string? PersonName,
    string? CardNumber, string? CardExp, int? ExpMonth, int? ExpYear, string? CardCvv,
    string? BankName, string? BankType, string? BankHolderType, string? BankAccount, string? BankRouting,
    string? Address1, string? Address2, string? City, string? State, string? Zip, string? Country);

public record PlaceOrderRequest(List<OrderLineRequest>? Items, AddressInput? ShipToAddress);
public record OrderLineRequest(int CatalogItemId, int Units);
public record PayWithMethodRequest(string? PaymentMethodId);

/// <summary>Runs a handler for the shopper named by the JWT; a token whose account no longer exists is unauthorized.</summary>
internal static class ShopperEndpoint
{
    public static async Task<IResult> RunAsync(ClaimsPrincipal user, ShopperAccess access, System.Func<Shopper, Task<IResult>> handler)
    {
        var shopper = await access.FindAsync(user);
        return shopper is null ? Results.Unauthorized() : await handler(shopper);
    }

    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;
}

/// <summary>Saves a card or a bank account for the signed-in shopper</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = ShopperEndpoint.Scheme)] async
            (PaymentMethodRequest request, ClaimsPrincipal user, ShopperAccess access, PaymentMethodService methods, CancellationToken ct) =>
                await ShopperEndpoint.RunAsync(user, access, async shopper =>
                {
                    var saved = await methods.SaveAsync(shopper, new SavePaymentMethodInput(
                        request.Method, request.PersonName, request.CardNumber, request.CardExp, request.ExpMonth, request.ExpYear,
                        request.CardCvv, request.BankName, request.BankType, request.BankHolderType, request.BankAccount,
                        request.BankRouting, request.Address1, request.Address2, request.City, request.State, request.Zip,
                        request.Country), ct);
                    return Results.Created($"api/payment-methods/{saved.Id}", saved);
                }))
            .Produces<PaymentMethodView>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Lists the signed-in shopper's saved cards and bank accounts</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = ShopperEndpoint.Scheme)] async
            (ClaimsPrincipal user, ShopperAccess access, PaymentMethodService methods, CancellationToken ct) =>
                await ShopperEndpoint.RunAsync(user, access, async shopper =>
                    Results.Ok(await methods.ListAsync(shopper, ct))))
            .Produces<IReadOnlyList<PaymentMethodView>>()
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Removes one of the signed-in shopper's saved payment methods</summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = ShopperEndpoint.Scheme)] async
            (string paymentMethodId, ClaimsPrincipal user, ShopperAccess access, PaymentMethodService methods, CancellationToken ct) =>
                await ShopperEndpoint.RunAsync(user, access, async shopper =>
                {
                    await methods.RemoveAsync(shopper, paymentMethodId, ct);
                    return Results.NoContent();
                }))
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Places an order for catalog items</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = ShopperEndpoint.Scheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, ShopperAccess access, OrderPaymentService orders, CancellationToken ct) =>
                await ShopperEndpoint.RunAsync(user, access, async shopper =>
                {
                    var lines = request.Items?.ConvertAll(i => new OrderLineInput(i.CatalogItemId, i.Units));
                    var order = await orders.PlaceOrderAsync(shopper, lines, request.ShipToAddress, ct);
                    return Results.Created($"api/orders/{order.OrderId}", order);
                }))
            .Produces<OrderView>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Shows the exact amount (fee and total) the shopper would pay with the chosen payment method</summary>
public class CheckoutEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/checkout",
            [Authorize(AuthenticationSchemes = ShopperEndpoint.Scheme)] async
            (int orderId, PayWithMethodRequest request, ClaimsPrincipal user, ShopperAccess access, OrderPaymentService orders, CancellationToken ct) =>
                await ShopperEndpoint.RunAsync(user, access, async shopper =>
                    Results.Ok(await orders.CheckoutAsync(shopper, orderId, request.PaymentMethodId, ct))))
            .Produces<CheckoutQuote>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Pays an order with a saved card or bank account</summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = ShopperEndpoint.Scheme)] async
            (int orderId, PayWithMethodRequest request, ClaimsPrincipal user, ShopperAccess access, OrderPaymentService orders, CancellationToken ct) =>
                await ShopperEndpoint.RunAsync(user, access, async shopper =>
                    Results.Ok(await orders.PayAsync(shopper, orderId, request.PaymentMethodId, ct))))
            .Produces<PayResult>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Shows an order of the signed-in shopper, with whether it has been paid</summary>
public class GetOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}",
            [Authorize(AuthenticationSchemes = ShopperEndpoint.Scheme)] async
            (int orderId, ClaimsPrincipal user, ShopperAccess access, OrderPaymentService orders, CancellationToken ct) =>
                await ShopperEndpoint.RunAsync(user, access, async shopper =>
                    Results.Ok(await orders.GetOrderAsync(shopper, orderId, ct))))
            .Produces<OrderView>()
            .WithTags("OrderEndpoints");
    }
}

/// <summary>Closes the signed-in shopper's account and erases the shopper's data, at Stax and in the shop</summary>
public class CloseAccountEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/account/close",
            [Authorize(AuthenticationSchemes = ShopperEndpoint.Scheme)] async
            (ClaimsPrincipal user, ShopperAccess access, AccountService accounts, CancellationToken ct) =>
                await ShopperEndpoint.RunAsync(user, access, async shopper =>
                {
                    await accounts.CloseAsync(shopper, ct);
                    return Results.NoContent();
                }))
            .WithTags("AccountEndpoints");
    }
}
