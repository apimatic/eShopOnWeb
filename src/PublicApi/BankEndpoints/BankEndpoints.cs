using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Plaid;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

/// <summary>
/// Pay by bank, bank activity and portfolio, backed by the Plaid API.
/// </summary>
public class BankEndpoints : IEndpoint
{
    private const string Shopper = JwtBearerDefaults.AuthenticationScheme;
    private const string Staff = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        // 1. Bank picker
        app.MapGet("api/banks",
            [Authorize(AuthenticationSchemes = Shopper)] async
            (string? query, int? count, int? offset, BankDirectoryService banks) =>
                Results.Ok(await banks.GetBanksAsync(query, count, offset)))
            .WithTags("BankEndpoints");

        // 2. Connect a bank
        app.MapPost("api/bank/link-token",
            [Authorize(AuthenticationSchemes = Shopper)] async
            (ClaimsPrincipal user, LinkTokenRequest? request, BankConnectionService connections) =>
                Results.Ok(await connections.CreateLinkTokenAsync(
                    CallerIdentity.BuyerKey(user), BankConnectionService.ParsePurpose(request?.Purpose))))
            .WithTags("BankEndpoints");

        app.MapPost("api/bank/connections",
            [Authorize(AuthenticationSchemes = Shopper)] async
            (ClaimsPrincipal user, ConnectBankRequest request, BankConnectionService connections) =>
            {
                var (connection, created) = await connections.ConnectAsync(CallerIdentity.BuyerKey(user), request);
                return created
                    ? Results.Created($"api/bank/connections/{connection.Id}", connection)
                    : Results.Ok(connection);
            })
            .WithTags("BankEndpoints");

        app.MapGet("api/bank/connections",
            [Authorize(AuthenticationSchemes = Shopper)] async
            (ClaimsPrincipal user, BankConnectionService connections) =>
                Results.Ok(await connections.ListAsync(CallerIdentity.BuyerKey(user))))
            .WithTags("BankEndpoints");

        // 3. Disconnect a bank
        app.MapDelete("api/bank/connections/{connectionId}",
            [Authorize(AuthenticationSchemes = Shopper)] async
            (int connectionId, ClaimsPrincipal user, BankConnectionService connections) =>
            {
                await connections.DisconnectAsync(CallerIdentity.BuyerKey(user), connectionId);
                return Results.NoContent();
            })
            .WithTags("BankEndpoints");

        // 4. Start a bank payment
        app.MapPost("api/checkout/bank-payment",
            [Authorize(AuthenticationSchemes = Shopper)] async
            (ClaimsPrincipal user, StartBankPaymentRequest request, BankPaymentService payments) =>
                Results.Ok(await payments.StartAsync(CallerIdentity.BuyerKey(user), request)))
            .WithTags("BankEndpoints");

        // 5. Change the shipping
        app.MapPut("api/checkout/bank-payment/{checkoutId}/shipping",
            [Authorize(AuthenticationSchemes = Shopper)] async
            (Guid checkoutId, ClaimsPrincipal user, ChangeShippingRequest request, BankPaymentService payments) =>
                Results.Ok(await payments.ChangeShippingAsync(CallerIdentity.BuyerKey(user), checkoutId, request.ShippingCost)))
            .WithTags("BankEndpoints");

        // 6. Confirm
        app.MapPost("api/checkout/bank-payment/{checkoutId}/confirm",
            [Authorize(AuthenticationSchemes = Shopper)] async
            (Guid checkoutId, ClaimsPrincipal user, ConfirmBankPaymentRequest request, BankPaymentService payments) =>
                Results.Ok(await payments.ConfirmAsync(CallerIdentity.BuyerKey(user), CallerIdentity.RawName(user), checkoutId, request.Speed)))
            .WithTags("BankEndpoints");

        // 7. Cancel an order
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(AuthenticationSchemes = Shopper)] async
            (int orderId, ClaimsPrincipal user, BankPaymentService payments) =>
                Results.Ok(await payments.CancelOrderAsync(CallerIdentity.RawName(user), orderId)))
            .WithTags("BankEndpoints");

        // 8. Bank activity for support
        app.MapGet("api/support/shoppers/{email}/bank-activity",
            [Authorize(Roles = Staff, AuthenticationSchemes = Shopper)] async
            (string email, BankDataService data) =>
                Results.Ok(await data.GetBankActivityAsync(email)))
            .WithTags("BankEndpoints");

        // 9. Transactions between two dates
        app.MapGet("api/support/shoppers/{email}/transactions",
            [Authorize(Roles = Staff, AuthenticationSchemes = Shopper)] async
            (string email, string? from, string? to, BankDataService data) =>
            {
                var start = ParseDate(from, "from");
                var end = ParseDate(to, "to");
                if (start > end)
                {
                    throw new ApiException(400, "from must not be after to.");
                }
                return Results.Ok(await data.GetTransactionsBetweenAsync(email, start, end));
            })
            .WithTags("BankEndpoints");

        // 10. Portfolio
        app.MapGet("api/portfolio",
            [Authorize(AuthenticationSchemes = Shopper)] async
            (ClaimsPrincipal user, BankDataService data) =>
                Results.Ok(await data.GetPortfolioAsync(CallerIdentity.BuyerKey(user))))
            .WithTags("BankEndpoints");
    }

    private static DateTime ParseDate(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ApiException(400, $"{name} is required, as YYYY-MM-DD.");
        }
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new ApiException(400, $"{name} must be a date as YYYY-MM-DD.");
        }
        return date;
    }
}
