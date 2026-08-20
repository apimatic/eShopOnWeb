using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    internal DateTimeOffset From { get; set; }
    internal DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public object[] Matched { get; set; } = Array.Empty<object>();
    public object[] PayPalOnly { get; set; } = Array.Empty<object>();
    public object[] EShopOnly { get; set; } = Array.Empty<object>();
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest
                {
                    From = ParseInstant(from, "from"),
                    To = ParseInstant(to, "to")
                }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.Matched.Count,
            PayPalOnlyCount = report.PayPalOnly.Count,
            EShopOnlyCount = report.EShopOnly.Count,
            Matched = report.Matched.Select(m => new
            {
                paypal = m.PayPal,
                eShop = m.EShop
            }).Cast<object>().ToArray(),
            PayPalOnly = report.PayPalOnly.Cast<object>().ToArray(),
            EShopOnly = report.EShopOnly.Cast<object>().ToArray()
        });
    }

    private static DateTimeOffset ParseInstant(string value, string name)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new PaymentException($"'{name}' must be an ISO-8601 date-time.");
        }

        return parsed;
    }
}
