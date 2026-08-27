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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class OrderNotificationEndpoints : IEndpoint
{
    private const string ShopperPolicy = JwtBearerDefaults.AuthenticationScheme;
    private const string OperatorRole = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers", RegisterContactNumber)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotifications");

        app.MapGet("api/contact-numbers", GetContactNumbers)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy })
            .WithTags("OrderNotifications");

        app.MapDelete("api/contact-numbers/{contactNumberId:int}", DeleteContactNumber)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy })
            .WithTags("OrderNotifications");

        app.MapPost("api/orders", PlaceOrder)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotifications");

        app.MapPost("api/orders/{orderId:int}/dispatch", DispatchOrder)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy, Roles = OperatorRole })
            .WithTags("OrderNotifications");

        app.MapPost("api/orders/{orderId:int}/cancel", CancelOrder)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy, Roles = OperatorRole })
            .WithTags("OrderNotifications");

        app.MapGet("api/my-orders", GetMyOrders)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy })
            .WithTags("OrderNotifications");

        app.MapGet("api/orders/{orderId:int}/notifications", GetOrderNotifications)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy })
            .WithTags("OrderNotifications");

        app.MapPost("api/notifications/{notificationId:int}/resend", ResendNotification)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy, Roles = OperatorRole })
            .Produces<ResendNotificationResponse>()
            .WithTags("OrderNotifications");

        app.MapDelete("api/notifications/{notificationId:int}/content", DisposeNotificationContent)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy, Roles = OperatorRole })
            .WithTags("OrderNotifications");

        app.MapGet("api/notifications/reconciliation", ReconcileNotifications)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ShopperPolicy, Roles = OperatorRole })
            .WithTags("OrderNotifications");
    }

    private static async Task<IResult> RegisterContactNumber(
        RegisterContactNumberRequest request,
        ClaimsPrincipal user,
        OrderNotificationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workflow.RegisterContactNumberAsync(RequiredBuyerId(user), request, cancellationToken);
            return Results.Created($"/api/contact-numbers/{response.ContactNumberId}", response);
        }
        catch (Exception ex) when (IsWorkflowException(ex)) { return Problem(ex); }
    }

    private static async Task<IResult> GetContactNumbers(
        ClaimsPrincipal user,
        OrderNotificationWorkflow workflow,
        CancellationToken cancellationToken) =>
        Results.Ok(await workflow.GetContactNumbersAsync(RequiredBuyerId(user), cancellationToken));

    private static async Task<IResult> DeleteContactNumber(
        int contactNumberId,
        ClaimsPrincipal user,
        OrderNotificationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            return await workflow.DeleteContactNumberAsync(RequiredBuyerId(user), contactNumberId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        }
        catch (Exception ex) when (IsWorkflowException(ex)) { return Problem(ex); }
    }

    private static async Task<IResult> PlaceOrder(
        PlaceOrderRequest request,
        ClaimsPrincipal user,
        OrderNotificationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workflow.PlaceOrderAsync(RequiredBuyerId(user), request, cancellationToken);
            return Results.Created($"/api/orders/{response.OrderId}", response);
        }
        catch (Exception ex) when (IsWorkflowException(ex)) { return Problem(ex); }
    }

    private static async Task<IResult> DispatchOrder(
        int orderId,
        OrderNotificationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workflow.DispatchAsync(orderId, cancellationToken);
            return response is null ? Results.NotFound() : Results.Ok(response);
        }
        catch (Exception ex) when (IsWorkflowException(ex)) { return Problem(ex); }
    }

    private static async Task<IResult> CancelOrder(
        int orderId,
        OrderNotificationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await workflow.CancelAsync(orderId, cancellationToken);
            return response is null ? Results.NotFound() : Results.Ok(response);
        }
        catch (Exception ex) when (IsWorkflowException(ex)) { return Problem(ex); }
    }

    private static async Task<IResult> GetMyOrders(
        ClaimsPrincipal user,
        OrderNotificationWorkflow workflow,
        CancellationToken cancellationToken) =>
        Results.Ok(await workflow.GetMyOrdersAsync(RequiredBuyerId(user), cancellationToken));

    private static async Task<IResult> GetOrderNotifications(
        int orderId,
        ClaimsPrincipal user,
        OrderNotificationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        var response = await workflow.GetOrderNotificationsAsync(RequiredBuyerId(user), orderId, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static async Task<IResult> ResendNotification(
        int notificationId,
        ResendNotificationRequest request,
        OrderNotificationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = await workflow.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
            return id is null ? Results.NotFound() : Results.Ok(new ResendNotificationResponse(id.Value));
        }
        catch (Exception ex) when (IsWorkflowException(ex)) { return Problem(ex); }
    }

    private static async Task<IResult> DisposeNotificationContent(
        int notificationId,
        OrderNotificationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            return await workflow.DisposeContentAsync(notificationId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        }
        catch (Exception ex) when (IsWorkflowException(ex)) { return Problem(ex); }
    }

    private static async Task<IResult> ReconcileNotifications(
        DateTimeOffset from,
        DateTimeOffset to,
        OrderNotificationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await workflow.ReconcileAsync(from, to, cancellationToken));
        }
        catch (Exception ex) when (IsWorkflowException(ex)) { return Problem(ex); }
    }

    private static string RequiredBuyerId(ClaimsPrincipal user) =>
        user.Identity?.Name ?? throw new InvalidOperationException("The authenticated token has no name claim.");

    private static bool IsWorkflowException(Exception ex) => ex is
        WorkflowValidationException or WorkflowConflictException or WorkflowProviderUnavailableException;

    private static IResult Problem(Exception ex) => ex switch
    {
        WorkflowValidationException validation => Results.ValidationProblem(
            new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["request"] = validation.Details.Count == 0
                    ? new[] { validation.Message }
                    : new[] { validation.Message }.Concat(validation.Details).ToArray()
            }),
        WorkflowConflictException conflict => Results.Conflict(new { error = conflict.Message }),
        WorkflowProviderUnavailableException unavailable => Results.Problem(
            unavailable.Message,
            statusCode: StatusCodes.Status502BadGateway),
        _ => Results.Problem()
    };
}
