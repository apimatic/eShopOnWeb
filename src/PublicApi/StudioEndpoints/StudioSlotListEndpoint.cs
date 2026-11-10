using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

public class StudioSlotListRequest
{
    public int SessionTypeId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
}

/// <summary>
/// Lists the times a one-to-one session of a type can start on each day between two dates
/// </summary>
public class StudioSlotListEndpoint : IEndpoint<IResult, StudioSlotListRequest, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int? sessionTypeId, string? from, string? to, StudioService service, CancellationToken cancellationToken) =>
            {
                if (sessionTypeId is null or <= 0)
                {
                    throw StudioException.BadRequest("'sessionTypeId' is required.");
                }
                var first = StudioDates.Parse(from, "from") ?? throw StudioException.BadRequest("'from' is required.");
                var last = StudioDates.Parse(to, "to") ?? throw StudioException.BadRequest("'to' is required.");

                var request = new StudioSlotListRequest { SessionTypeId = sessionTypeId.Value, From = first, To = last };
                return await HandleAsync(request, service, cancellationToken);
            })
            .Produces<IReadOnlyList<StudioSlotDto>>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioSlotListRequest request, StudioService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(StudioSlotListRequest request, StudioService service, CancellationToken cancellationToken)
    {
        var slots = await service.GetSlotsAsync(request.SessionTypeId, request.From, request.To, cancellationToken);
        return Results.Ok(slots);
    }
}
