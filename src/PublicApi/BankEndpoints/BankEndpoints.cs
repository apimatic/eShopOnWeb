using System;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using Microsoft.eShopWeb.PublicApi.Plaid;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

public record LinkTokenRequest(string? Type);
public record SaveConnectionRequest(string? PublicToken, string? Type, string? AccountId);

/// <summary>Feature 1: the checkout bank picker.</summary>
public class BankListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? query, int? count, int? offset, InstitutionService institutions, CancellationToken ct) =>
                Results.Ok(await institutions.GetAsync(query, count, offset, ct)))
            .Produces<BankPage>()
            .WithTags("BankEndpoints");
    }
}

/// <summary>Feature 2: what the browser needs to open Plaid Link.</summary>
public class CreateLinkTokenEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/link-token",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (LinkTokenRequest? request, ClaimsPrincipal user, ConnectionService connections, CancellationToken ct) =>
                Results.Ok(await connections.CreateLinkTokenAsync(CurrentUser.Name(user), request?.Type, ct)))
            .Produces<LinkTokenResult>()
            .WithTags("BankEndpoints");
    }
}

/// <summary>Feature 2: save a connection from what Link hands the browser.</summary>
public class SaveConnectionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/bank/connections",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SaveConnectionRequest request, ClaimsPrincipal user, ConnectionService connections, CancellationToken ct) =>
            {
                var saved = await connections.SaveAsync(CurrentUser.Name(user), request.PublicToken, request.Type, request.AccountId, ct);
                return Results.Created($"api/bank/connections/{saved.ConnectionId}", saved);
            })
            .Produces<ConnectionDto>(StatusCodes.Status201Created)
            .WithTags("BankEndpoints");
    }
}

/// <summary>Feature 3: disconnect a bank or brokerage account.</summary>
public class DisconnectEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/bank/connections/{connectionId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Guid connectionId, ClaimsPrincipal user, ConnectionService connections, CancellationToken ct) =>
            {
                await connections.DisconnectAsync(CurrentUser.Name(user), connectionId, ct);
                return Results.NoContent();
            })
            .WithTags("BankEndpoints");
    }
}

/// <summary>Feature 4: start a bank payment.</summary>
public class StartBankPaymentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (StartCheckoutRequest request, ClaimsPrincipal user, CheckoutService checkout, CancellationToken ct) =>
            {
                var view = await checkout.StartAsync(CurrentUser.Name(user), request, ct);
                return Results.Created($"api/checkout/bank-payment/{view.CheckoutId}", view);
            })
            .Produces<CheckoutView>(StatusCodes.Status201Created)
            .WithTags("BankEndpoints");
    }
}

/// <summary>Feature 5: change the shipping cost of a started payment.</summary>
public class ChangeBankPaymentShippingEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Guid checkoutId, ChangeShippingRequest request, ClaimsPrincipal user, CheckoutService checkout, CancellationToken ct) =>
                Results.Ok(await checkout.ChangeShippingAsync(CurrentUser.Name(user), checkoutId, request.ShippingCost, ct)))
            .Produces<CheckoutView>()
            .WithTags("BankEndpoints");
    }
}

/// <summary>Feature 6: confirm and charge.</summary>
public class ConfirmBankPaymentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Guid checkoutId, ConfirmRequest request, ClaimsPrincipal user, CheckoutService checkout, CancellationToken ct) =>
                Results.Ok(await checkout.ConfirmAsync(CurrentUser.Name(user), checkoutId, request.Speed, ct)))
            .Produces<OrderConfirmation>()
            .WithTags("BankEndpoints");
    }
}

/// <summary>Feature 7: cancel an order paid by bank, before it ships.</summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, CheckoutService checkout, CancellationToken ct) =>
                Results.Ok(await checkout.CancelAsync(CurrentUser.Name(user), orderId, ct)))
            .Produces<CancelResult>()
            .WithTags("BankEndpoints");
    }
}

/// <summary>Staff: mark a bank-paid order as shipped (it can no longer be cancelled).</summary>
public class ShipOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/ship",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CheckoutService checkout, CancellationToken ct) =>
            {
                await checkout.MarkShippedAsync(orderId, ct);
                return Results.NoContent();
            })
            .WithTags("BankEndpoints");
    }
}

/// <summary>Feature 8: the shop's copy of a shopper's bank activity, for support.</summary>
public class BankActivityEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, ShopperResolver shoppers, TransactionService transactions, CancellationToken ct) =>
            {
                var userName = await shoppers.ResolveAsync(email);
                return Results.Ok(await transactions.GetActivityAsync(email, userName, ct));
            })
            .Produces<BankActivity>()
            .WithTags("BankEndpoints");
    }
}

/// <summary>Feature 9: transactions between two dates, straight from the bank.</summary>
public class BankTransactionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/transactions",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string email, string? from, string? to, ShopperResolver shoppers, TransactionService transactions, CancellationToken ct) =>
            {
                var start = ParseDate(from, "from");
                var end = ParseDate(to, "to");
                if (start > end) throw new ApiException(HttpStatusCode.BadRequest, "from must not be after to.");
                var userName = await shoppers.ResolveAsync(email);
                return Results.Ok(await transactions.GetRangeAsync(email, userName, start, end, ct));
            })
            .Produces<BankTransactions>()
            .WithTags("BankEndpoints");
    }

    private static DateTime ParseDate(string? value, string name)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new ApiException(HttpStatusCode.BadRequest, $"{name} is required and must be a date as YYYY-MM-DD.");
        }
        return date;
    }
}

/// <summary>Feature 10: the shopper's brokerage holdings.</summary>
public class PortfolioEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/portfolio",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, PortfolioService portfolio, CancellationToken ct) =>
                Results.Ok(new { positions = await portfolio.GetAsync(CurrentUser.Name(user), ct) }))
            .WithTags("BankEndpoints");
    }
}
