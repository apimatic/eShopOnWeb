using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Studio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// Lists the studio's classes a shopper can attend
/// </summary>
public class ListClassesEndpoint : IEndpoint<IResult, ListClassesQuery, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, StudioService studio) =>
            {
                return await HandleAsync(new ListClassesQuery(from, to), studio);
            })
            .Produces<ListClassesResponse>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(ListClassesQuery request, StudioService studio) =>
        StudioResults.RunAsync(async () =>
        {
            var response = new ListClassesResponse();
            response.Classes.AddRange(await studio.ListClassesAsync(request.From, request.To));
            return Results.Ok(response);
        });
}

/// <summary>
/// Lists the times a one-to-one session of a type can start, day by day
/// </summary>
public class ListSlotsEndpoint : IEndpoint<IResult, ListSlotsQuery, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/slots",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int? sessionTypeId, string? from, string? to, StudioService studio) =>
            {
                return await HandleAsync(new ListSlotsQuery(sessionTypeId, from, to), studio);
            })
            .Produces<ListSlotsResponse>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(ListSlotsQuery request, StudioService studio) =>
        StudioResults.RunAsync(async () =>
        {
            var response = new ListSlotsResponse { SessionTypeId = request.SessionTypeId ?? 0 };
            response.Slots.AddRange(await studio.ListSlotsAsync(request.SessionTypeId, request.From, request.To));
            return Results.Ok(response);
        });
}
