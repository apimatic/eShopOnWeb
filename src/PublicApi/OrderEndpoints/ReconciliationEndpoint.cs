using System;
using System.Globalization;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationEndpoint.Request, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
                [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (string? from, string? to, [Microsoft.AspNetCore.Mvc.FromServices] IOrderPaymentService service) =>
                    await HandleAsync(new Request { From = from, To = to }, service))
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IOrderPaymentService service)
    {
        if (!TryParse(request.From, out var from) || !TryParse(request.To, out var to))
        {
            return Results.BadRequest(new
            {
                statusCode = 400,
                message = "'from' and 'to' are required ISO-8601 date-times.",
                code = "INVALID_DATE_RANGE"
            });
        }

        var response = await service.ReconcileAsync(from, to, default);
        return Results.Ok(response);
    }

    private static bool TryParse(string? value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);

    public class Request
    {
        public string? From { get; set; }
        public string? To { get; set; }
    }
}
