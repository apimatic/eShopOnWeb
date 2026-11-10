using System;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

/// <summary>Support staff's view of a shopper's connected banks.</summary>
public class SupportBankEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/support/shoppers/{email}/bank-activity",
                [Authorize(Roles = Shopper.Admins, AuthenticationSchemes = Shopper.Scheme)] async (string email,
                    UserManager<ApplicationUser> users, BankActivityService activity) =>
                {
                    var shopper = await Shopper.FindAsync(users, email);
                    var result = await activity.GetActivityAsync(shopper);
                    return Results.Ok(new { email, transactions = result.Transactions, unavailable = result.Unavailable });
                })
            .WithTags("BankEndpoints");

        app.MapGet("api/support/shoppers/{email}/transactions",
                [Authorize(Roles = Shopper.Admins, AuthenticationSchemes = Shopper.Scheme)] async (string email,
                    HttpRequest http, UserManager<ApplicationUser> users, BankActivityService activity) =>
                {
                    var from = ParseDate(http.Query["from"].ToString(), "from");
                    var to = ParseDate(http.Query["to"].ToString(), "to");
                    if (from > to)
                    {
                        throw new ApiRequestException(HttpStatusCode.BadRequest, "from must not be after to.");
                    }

                    var shopper = await Shopper.FindAsync(users, email);
                    var result = await activity.GetTransactionsAsync(shopper, from, to);
                    return Results.Ok(new
                    {
                        email,
                        from = BankActivityService.FormatDate(from),
                        to = BankActivityService.FormatDate(to),
                        transactions = result.Transactions,
                        unavailable = result.Unavailable
                    });
                })
            .WithTags("BankEndpoints");
    }

    private static DateTime ParseDate(string value, string name) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ApiRequestException(HttpStatusCode.BadRequest, $"{name} must be a date as YYYY-MM-DD.");
}
