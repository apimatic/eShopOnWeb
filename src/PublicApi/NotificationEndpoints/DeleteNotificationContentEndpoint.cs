using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, HttpContext http) => await HandleAsync(notificationId, http))
            .Produces<Response>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, HttpContext http)
    {
        try
        {
            await http.GetRequired<IOrderNotificationService>().RedactContentAsync(notificationId);
            return Results.Ok(new Response
            {
                NotificationId = notificationId,
                ContentRedacted = true
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (TwilioApiException ex)
        {
            return Results.Json(new { error = "The provider could not dispose of the message content." },
                statusCode: ex.StatusCode >= 400 ? ex.StatusCode : StatusCodes.Status502BadGateway);
        }
    }

    public class Response
    {
        public int NotificationId { get; set; }
        public bool ContentRedacted { get; set; }
    }
}
