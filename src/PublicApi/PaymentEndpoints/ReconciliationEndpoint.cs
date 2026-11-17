using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, ClaimsPrincipal>
{
    private readonly FinixPaymentService _payments;

    public ReconciliationEndpoint(FinixPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
                [Authorize(
                    Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (DateTimeOffset? from, DateTimeOffset? to, ClaimsPrincipal user) =>
                {
                    var request = new ReconciliationRequest { From = from, To = to };
                    return await HandleAsync(request, user);
                })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, ClaimsPrincipal user)
    {
        if (request.From is null || request.To is null)
        {
            return Results.BadRequest(new
            {
                message = "Both from and to ISO-8601 date-times are required."
            });
        }

        try
        {
            var report = await _payments.ReconcileAsync(
                request.From.Value,
                request.To.Value,
                CancellationToken.None);

            var response = new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                FinixTransactions = report.FinixTransactions,
                EshopTransactions = report.EshopTransactions,
                UnmatchedFinixTransactions = report.UnmatchedFinixTransactions,
                UnmatchedEshopTransactions = report.UnmatchedEshopTransactions
            };

            return Results.Ok(response);
        }
        catch (PaymentOperationException exception)
        {
            return PaymentResults.FromException(exception);
        }
    }
}
