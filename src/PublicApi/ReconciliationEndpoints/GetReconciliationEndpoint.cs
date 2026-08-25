using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using BlazorShared.Authorization;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IRepository<Order> orderRepo, IPayPalService payPal) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromDate) || !DateTimeOffset.TryParse(to, out var toDate))
                    return Results.BadRequest("Invalid date format. Use ISO 8601 (e.g. 2024-01-01T00:00:00Z).");

                if (toDate < fromDate)
                    return Results.BadRequest("'to' must be after 'from'.");

                // PayPal requires ISO 8601 with timezone offset
                var startDate = fromDate.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
                var endDate = toDate.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

                var transactions = await payPal.GetTransactionsAsync(startDate, endDate);
                var orders = await orderRepo.ListAsync(new OrdersInDateRangeSpec(fromDate, toDate));

                var orderLookup = orders
                    .Where(o => o.Payment?.PayPalOrderId != null)
                    .ToDictionary(o => o.Payment!.PayPalOrderId!);

                var rows = transactions.Select(t =>
                {
                    orderLookup.TryGetValue(t.TransactionId ?? "", out var matchedOrder);
                    return new ReconciliationRow
                    {
                        TransactionId = t.TransactionId,
                        Amount = t.Amount,
                        Currency = t.Currency,
                        Fee = t.Fee,
                        Status = t.Status,
                        InitiationDate = t.InitiationDate,
                        OrderId = matchedOrder?.Id,
                        BuyerId = matchedOrder?.BuyerId,
                        OrderStatus = matchedOrder?.Status.ToString()
                    };
                }).ToList();

                return Results.Ok(new ReconciliationResponse
                {
                    From = from,
                    To = to,
                    TotalTransactions = rows.Count,
                    Rows = rows
                });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }
}
