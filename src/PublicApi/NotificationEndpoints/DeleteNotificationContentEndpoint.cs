using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: dispose of the content of a message about a shopper. Afterwards the text is no
/// longer retrievable from the provider either, while the fact that a message was sent — and what
/// became of it — survives. Admin only.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int>
{
    private readonly IOrderMessagingService _orderMessagingService;

    public DeleteNotificationContentEndpoint(IOrderMessagingService orderMessagingService)
    {
        _orderMessagingService = orderMessagingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId) => await HandleAsync(notificationId))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId)
    {
        using var cts = EndpointBudget.Start();
        try
        {
            var found = await _orderMessagingService.RedactContentAsync(notificationId, cts.Token);
            return found ? Results.NoContent() : Results.NotFound();
        }
        catch (SmsGatewayException ex)
        {
            // The provider copy could not be disposed of — surface it to the operator.
            return Results.Problem(
                title: "The messaging provider could not dispose of the content.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
