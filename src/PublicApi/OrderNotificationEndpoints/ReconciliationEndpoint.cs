using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// Operator report: the provider's own record of messages for a date range, lined up against what eShop
/// believes it sent. Only messages sent from this application's configured sending number are counted (asked
/// of the provider directly). from/to are ISO-8601 date-times and the whole range is covered.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, HttpContext>
{
    private readonly IOrderNotificationService _service;

    public ReconciliationEndpoint(IOrderNotificationService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, HttpContext http) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), http);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, HttpContext http)
    {
        if (request.From > request.To)
        {
            return Results.BadRequest("'from' must be on or before 'to'.");
        }

        try
        {
            var report = await _service.ReconcileAsync(request.From, request.To, http.RequestAborted);
            return Results.Ok(ReconciliationResponse.Create(report, request.CorrelationId()));
        }
        catch (SmsProviderException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
