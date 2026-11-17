using System;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

using Microsoft.eShopWeb.PublicApi;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
                [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (DateTimeOffset? from, DateTimeOffset? to, HttpContext http, PaymentService payments) =>
                {
                    var request = new ReconciliationRequest
                    {
                        From = from,
                        To = to,
                        RequestAborted = http.RequestAborted
                    };
                    return await HandleAsync(request, payments);
                })
            .Produces<ReconciliationReport>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, PaymentService payments)
    {
        var to = request.To ?? DateTimeOffset.UtcNow;
        var from = request.From ?? to.AddDays(-30);

        var report = await payments.ReconcileAsync(from, to, request.RequestAborted);
        return Results.Ok(report);
    }
}

public class ReconciliationRequest : PaymentRequestBase
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}
