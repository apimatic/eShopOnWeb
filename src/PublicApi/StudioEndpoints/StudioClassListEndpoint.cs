using System;
using System.Collections.Generic;
using System.Globalization;
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

public class StudioClassListRequest
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

/// <summary>
/// Lists the studio's classes a shopper can attend, for a range of days (default: today and the next 7 days)
/// </summary>
public class StudioClassListEndpoint : IEndpoint<IResult, StudioClassListRequest, StudioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/studio/classes",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, StudioService service, CancellationToken cancellationToken) =>
            {
                var request = new StudioClassListRequest
                {
                    From = StudioDates.Parse(from, "from"),
                    To = StudioDates.Parse(to, "to"),
                };
                return await HandleAsync(request, service, cancellationToken);
            })
            .Produces<IReadOnlyList<StudioClassDto>>()
            .WithTags("StudioEndpoints");
    }

    public Task<IResult> HandleAsync(StudioClassListRequest request, StudioService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(StudioClassListRequest request, StudioService service, CancellationToken cancellationToken)
    {
        var classes = await service.GetClassesAsync(request.From, request.To, cancellationToken);
        return Results.Ok(classes);
    }
}

internal static class StudioDates
{
    public static DateOnly? Parse(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return date;
        }
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var moment))
        {
            return DateOnly.FromDateTime(moment.DateTime);
        }
        throw StudioException.BadRequest($"'{name}' is not a date. Use the form yyyy-MM-dd.");
    }
}
