using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public sealed class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    RegisterContactNumberRequest request,
                    ClaimsPrincipal principal,
                    OrderNotificationService service,
                    CancellationToken cancellationToken) =>
                {
                    if (!EndpointResults.TryGetBuyerId(principal, out var buyerId))
                    {
                        return Results.Unauthorized();
                    }

                    try
                    {
                        var id = await service.RegisterContactNumberAsync(buyerId, request.PhoneNumber, cancellationToken);
                        return Results.Created($"/api/contact-numbers/{id}", new RegisterContactNumberResponse(id));
                    }
                    catch (InvalidDestinationException ex)
                    {
                        return EndpointResults.BadRequest(ex.Message);
                    }
                    catch (ContactNumberAlreadyRegisteredException ex)
                    {
                        return EndpointResults.Conflict(ex.Message);
                    }
                    catch (TwilioProviderException)
                    {
                        return EndpointResults.ProviderUnavailable();
                    }
                })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("ContactNumberEndpoints");
    }
}

public sealed class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    ClaimsPrincipal principal,
                    OrderNotificationService service,
                    CancellationToken cancellationToken) =>
                {
                    if (!EndpointResults.TryGetBuyerId(principal, out var buyerId))
                    {
                        return Results.Unauthorized();
                    }

                    var contacts = await service.GetContactNumbersAsync(buyerId, cancellationToken);
                    return Results.Ok(new ContactNumberListResponse(
                        contacts.Select(contact => new ContactNumberDto(contact.Id, contact.PhoneNumber, contact.CreatedAt)).ToList()));
                })
            .Produces<ContactNumberListResponse>()
            .WithTags("ContactNumberEndpoints");
    }
}

public sealed class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                    int contactNumberId,
                    ClaimsPrincipal principal,
                    OrderNotificationService service,
                    CancellationToken cancellationToken) =>
                {
                    if (!EndpointResults.TryGetBuyerId(principal, out var buyerId))
                    {
                        return Results.Unauthorized();
                    }

                    var deleted = await service.DeleteContactNumberAsync(buyerId, contactNumberId, cancellationToken);
                    return deleted ? Results.NoContent() : Results.NotFound();
                })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }
}
