using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Shows the caller's metered usage on a single day
/// </summary>
public class SubscriptionUsageEndpoint : IEndpoint
{
    private const int PageSize = 200;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/{subscriptionId}/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, string? date, ClaimsPrincipal user, BillingService billing) =>
            {
                return await HandleAsync(subscriptionId, date, user, billing);
            })
            .Produces<DailyUsageResponse>()
            .WithTags("BillingEndpoints");
    }

    private static Task<IResult> HandleAsync(int subscriptionId, string? date, ClaimsPrincipal user, BillingService billing)
    {
        return BillingException.RunAsync(async () =>
        {
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                throw BillingException.BadRequest("The 'date' query parameter is required, formatted yyyy-MM-dd.");
            var day = DateOnly.FromDateTime(parsed);

            await billing.GetOwnedSubscriptionAsync(subscriptionId, user);

            var components = await billing.Client.SubscriptionComponents.ListSubscriptionComponents(new ListSubscriptionComponentsRequest
            {
                SubscriptionId = subscriptionId,
            });

            var response = new DailyUsageResponse { SubscriptionId = subscriptionId, Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };

            foreach (var component in components.Select(c => c.Component).Where(c => c?.Kind == ComponentKind.MeteredComponent && c.ComponentId != null))
            {
                var usage = new ComponentUsageDto
                {
                    ComponentId = component!.ComponentId,
                    ComponentHandle = component.ComponentHandle,
                    ComponentName = component.Name,
                    UnitName = component.UnitName,
                };

                // Maxio's date filters are coarse (and differ in how they treat the end of the range),
                // so ask for a window around the day and pick the day out by each record's own timestamp,
                // which Maxio reports in the site's time zone.
                var from = new DateTimeOffset(day.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                var until = new DateTimeOffset(day.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                for (var page = 1; ; page++)
                {
                    var batch = await billing.Client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                    {
                        SubscriptionIdOrReference = subscriptionId,
                        ComponentId = component.ComponentId!.Value,
                        SinceDate = from,
                        UntilDate = until,
                        Page = page,
                        PerPage = PageSize,
                    });

                    foreach (var u in batch.Select(b => b.Usage))
                    {
                        if (u.CreatedAt == null || DateOnly.FromDateTime(u.CreatedAt.Value.DateTime) != day) continue;
                        var quantity = ReadQuantity(u.Quantity);
                        usage.Quantity += quantity;
                        usage.Entries.Add(new UsageEntryDto { Id = u.Id, CreatedAt = u.CreatedAt, Quantity = quantity, Memo = u.Memo });
                    }

                    if (batch.Count < PageSize) break;
                }

                response.TotalQuantity += usage.Quantity;
                response.Components.Add(usage);
            }

            return Results.Ok(response);
        });
    }

    private static decimal ReadQuantity(Quantity1? quantity)
    {
        if (quantity == null) return 0;
        if (quantity.TryGetInt(out var i)) return i;
        if (quantity.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }
}
