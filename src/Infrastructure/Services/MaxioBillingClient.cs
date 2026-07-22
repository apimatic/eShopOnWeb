using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one class in the application that talks to Maxio Advanced Billing. It speaks the provider's
/// HTTP API directly, normalizes every result into the provider-agnostic billing models, and turns
/// every failure into a typed <see cref="BillingProviderException"/>.
/// </summary>
public class MaxioBillingClient : IBillingClient
{
    private const int MaxProviderMessageLength = 500;
    private const string PerUnitPricingScheme = "per_unit";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private static readonly HttpStatusCode[] TransientStatusCodes =
    {
        HttpStatusCode.RequestTimeout,
        (HttpStatusCode)429,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = RequireHandle(_settings.ProductFamilyHandle, nameof(MaxioSettings.ProductFamilyHandle));

        var products = await GetAsync<List<MaxioProductResponse>>(
            $"product_families/handle:{Uri.EscapeDataString(family)}/products.json", cancellationToken);

        if (products is null)
        {
            throw new BillingConfigurationException(
                $"Product family '{family}' does not exist in the billing provider. Re-seed it or correct the configured handle.");
        }

        return products
            .Select(p => p.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => ToPlan(p!))
            .ToList();
    }

    public async Task<BillingPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        RequireHandle(planHandle, nameof(planHandle));

        var response = await GetAsync<MaxioProductResponse>(
            $"products/handle/{Uri.EscapeDataString(planHandle)}.json", cancellationToken);

        return response?.Product is null ? null : ToPlan(response.Product);
    }

    public async Task<BillingCustomer?> FindCustomerByReferenceAsync(string userReference, CancellationToken cancellationToken = default)
    {
        RequireHandle(userReference, nameof(userReference));

        var response = await GetAsync<MaxioCustomerResponse>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(userReference)}", cancellationToken);

        return response?.Customer is null ? null : ToCustomer(response.Customer);
    }

    public async Task<BillingCustomer> CreateCustomerAsync(string userReference, string email, CancellationToken cancellationToken = default)
    {
        RequireHandle(userReference, nameof(userReference));

        var (firstName, lastName) = SplitName(email, userReference);
        var payload = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = string.IsNullOrWhiteSpace(email) ? userReference : email,
                Reference = userReference
            }
        };

        var response = await SendJsonAsync<MaxioCustomerResponse>(HttpMethod.Post, "customers.json", payload, cancellationToken);

        return response?.Customer is null
            ? throw new BillingProviderException("The billing provider accepted the customer but returned no customer record.")
            : ToCustomer(response.Customer);
    }

    public async Task<IReadOnlyCollection<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await GetAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);

        if (subscriptions is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return subscriptions
            .Select(s => s.Subscription)
            .Where(s => s is not null)
            .Select(s => ToSubscription(s!))
            .ToList();
    }

    public async Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync<MaxioSubscriptionResponse>($"subscriptions/{subscriptionId}.json", cancellationToken);
        return response?.Subscription is null ? null : ToSubscription(response.Subscription);
    }

    public async Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default)
    {
        RequireHandle(planHandle, nameof(planHandle));

        var payload = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription { ProductHandle = planHandle, CustomerId = customerId }
        };

        return await PostForSubscriptionAsync("subscriptions.json", payload, cancellationToken);
    }

    public async Task<BillingComponent?> GetComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default)
    {
        RequireHandle(componentHandle, nameof(componentHandle));

        var response = await GetAsync<MaxioComponentResponse>(
            $"components/lookup.json?handle={Uri.EscapeDataString(componentHandle)}", cancellationToken);

        return response?.Component is null ? null : ToComponent(response.Component);
    }

    public async Task<BillingUsageRecord> RecordUsageAsync(int subscriptionId, string componentHandle, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        RequireHandle(componentHandle, nameof(componentHandle));

        var payload = new MaxioCreateUsageRequest
        {
            Usage = new MaxioCreateUsage { Quantity = quantity, Memo = memo }
        };

        var response = await SendJsonAsync<MaxioUsageResponse>(
            HttpMethod.Post,
            $"subscriptions/{subscriptionId}/components/handle:{Uri.EscapeDataString(componentHandle)}/usages.json",
            payload,
            cancellationToken);

        if (response?.Usage is null)
        {
            throw new BillingProviderException("The billing provider accepted the usage but returned no usage record.");
        }

        return new BillingUsageRecord
        {
            Id = response.Usage.Id,
            SubscriptionId = response.Usage.SubscriptionId == 0 ? subscriptionId : response.Usage.SubscriptionId,
            ComponentId = response.Usage.ComponentId,
            ComponentHandle = response.Usage.ComponentHandle ?? componentHandle,
            Quantity = ReadQuantity(response.Usage.Quantity, quantity),
            Memo = response.Usage.Memo,
            CreatedAt = response.Usage.CreatedAt
        };
    }

    public async Task<BillingUsageTotal?> GetUsageTotalAsync(int subscriptionId, string componentHandle, CancellationToken cancellationToken = default)
    {
        RequireHandle(componentHandle, nameof(componentHandle));

        var response = await GetAsync<MaxioSubscriptionComponentResponse>(
            $"subscriptions/{subscriptionId}/components/handle:{Uri.EscapeDataString(componentHandle)}.json", cancellationToken);

        if (response?.Component is null)
        {
            return null;
        }

        var component = response.Component;
        return new BillingUsageTotal
        {
            SubscriptionId = component.SubscriptionId == 0 ? subscriptionId : component.SubscriptionId,
            ComponentId = component.ComponentId,
            ComponentHandle = component.ComponentHandle ?? componentHandle,
            Name = component.Name ?? string.Empty,
            Kind = component.Kind ?? string.Empty,
            UnitBalance = component.UnitBalance ?? 0m
        };
    }

    public async Task<BillingPlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        RequireHandle(targetPlanHandle, nameof(targetPlanHandle));

        var response = await SendJsonAsync<MaxioMigrationPreviewResponse>(
            HttpMethod.Post,
            $"subscriptions/{subscriptionId}/migrations/preview.json",
            BuildMigration(targetPlanHandle, timing),
            cancellationToken);

        if (response?.Migration is null)
        {
            throw new BillingProviderException("The billing provider returned no migration preview.");
        }

        var migration = response.Migration;
        return new BillingPlanChangePreview
        {
            SubscriptionId = subscriptionId,
            TargetProductHandle = targetPlanHandle,
            Prorate = timing == PlanChangeTiming.ImmediateWithProration,
            ProratedAdjustment = FromCents(migration.ProratedAdjustmentInCents),
            Charge = FromCents(migration.ChargeInCents),
            PaymentDue = FromCents(migration.PaymentDueInCents),
            CreditApplied = FromCents(migration.CreditAppliedInCents)
        };
    }

    public Task<BillingSubscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        RequireHandle(targetPlanHandle, nameof(targetPlanHandle));

        return PostForSubscriptionAsync(
            $"subscriptions/{subscriptionId}/migrations.json",
            BuildMigration(targetPlanHandle, timing),
            cancellationToken);
    }

    public Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => PostForSubscriptionAsync($"subscriptions/{subscriptionId}/hold.json", null, cancellationToken);

    public Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => PostForSubscriptionAsync($"subscriptions/{subscriptionId}/resume.json", null, cancellationToken);

    public Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => SendForSubscriptionAsync(HttpMethod.Put, $"subscriptions/{subscriptionId}/reactivate.json", null, cancellationToken);

    public Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default)
        => SendForSubscriptionAsync(HttpMethod.Delete, $"subscriptions/{subscriptionId}.json", BuildCancellation(reason), cancellationToken);

    public async Task<BillingSubscription> CancelSubscriptionAtEndOfPeriodAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default)
    {
        // The delayed-cancellation endpoint only acknowledges the request, so the subscription is
        // re-read afterwards to report the state the provider actually holds.
        await SendJsonAsync<JsonElement?>(
            HttpMethod.Post,
            $"subscriptions/{subscriptionId}/delayed_cancel.json",
            BuildCancellation(reason),
            cancellationToken);

        return await GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingEntityNotFoundException($"Subscription {subscriptionId} was not found after scheduling its cancellation.");
    }

    private static MaxioMigrationRequest BuildMigration(string targetPlanHandle, PlanChangeTiming timing) => new()
    {
        Migration = new MaxioMigrationOptions
        {
            ProductHandle = targetPlanHandle,
            // Preserving the period is what makes the provider prorate rather than restart the term.
            PreservePeriod = timing == PlanChangeTiming.ImmediateWithProration
        }
    };

    private static MaxioCancellationRequest BuildCancellation(string? reason) => new()
    {
        Subscription = new MaxioCancellationOptions { CancellationMessage = reason }
    };

    private Task<BillingSubscription> PostForSubscriptionAsync(string relativeUrl, object? payload, CancellationToken cancellationToken)
        => SendForSubscriptionAsync(HttpMethod.Post, relativeUrl, payload, cancellationToken);

    private async Task<BillingSubscription> SendForSubscriptionAsync(HttpMethod method, string relativeUrl, object? payload, CancellationToken cancellationToken)
    {
        var response = await SendJsonAsync<MaxioSubscriptionResponse>(method, relativeUrl, payload, cancellationToken);

        return response?.Subscription is null
            ? throw new BillingProviderException("The billing provider accepted the request but returned no subscription.")
            : ToSubscription(response.Subscription);
    }

    /// <summary>Reads a resource, mapping the provider's "no such thing" answer onto null.</summary>
    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, relativeUrl), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<T?> SendJsonAsync<T>(HttpMethod method, string relativeUrl, object? payload, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(() =>
        {
            var request = new HttpRequestMessage(method, relativeUrl);
            if (payload is not null)
            {
                request.Content = JsonContent.Create(payload, payload.GetType(), options: JsonOptions);
            }
            return request;
        }, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new BillingProviderException(
                "The billing provider returned a response that could not be understood.",
                (int)response.StatusCode,
                null,
                ex);
        }
    }

    /// <summary>
    /// Sends a request, retrying transient failures with exponential backoff within a bounded
    /// overall budget. Only reads are retried: resending a write whose outcome is unknown risks
    /// billing the customer twice, so a failed write surfaces immediately for the caller to
    /// re-read and decide. The request is rebuilt per attempt so retries carry fresh content.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(Math.Max(1, _settings.OverallTimeoutSeconds));

        for (var attempt = 1; ; attempt++)
        {
            TimeSpan delay;
            bool retryable;

            try
            {
                using var request = requestFactory();
                retryable = IsRetryable(request.Method);

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!retryable || !TransientStatusCodes.Contains(response.StatusCode))
                {
                    return response;
                }

                delay = ComputeDelay(attempt, response.Headers.RetryAfter?.Delta);
                if (IsExhausted(attempt, deadline, budget, delay))
                {
                    return response;
                }

                response.Dispose();
            }
            catch (HttpRequestException ex)
            {
                delay = ComputeDelay(attempt, null);
                if (!IsRetryable(requestFactory) || IsExhausted(attempt, deadline, budget, delay))
                {
                    throw new BillingProviderUnavailableException(
                        "The billing provider could not be reached.", null, null, ex);
                }
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                delay = ComputeDelay(attempt, null);
                if (!IsRetryable(requestFactory) || IsExhausted(attempt, deadline, budget, delay))
                {
                    throw new BillingProviderUnavailableException(
                        "The billing provider did not respond in time.", null, null, ex);
                }
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>Only side-effect-free reads may be repeated after an ambiguous failure.</summary>
    private static bool IsRetryable(HttpMethod method) => method == HttpMethod.Get || method == HttpMethod.Head;

    private static bool IsRetryable(Func<HttpRequestMessage> requestFactory)
    {
        using var probe = requestFactory();
        return IsRetryable(probe.Method);
    }

    private bool IsExhausted(int attempt, Stopwatch deadline, TimeSpan budget, TimeSpan delay)
        => attempt >= Math.Max(1, _settings.MaxRetryAttempts) || deadline.Elapsed + delay >= budget;

    private TimeSpan ComputeDelay(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
        {
            return retryAfter.Value;
        }

        var baseDelay = Math.Max(1, _settings.RetryBaseDelayMilliseconds);
        return TimeSpan.FromMilliseconds(baseDelay * Math.Pow(2, attempt - 1));
    }

    /// <summary>
    /// Turns a provider failure into the matching typed exception. Only the provider's own message
    /// travels outward - the request, its headers and the credential never do.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        var providerErrors = await ReadProviderErrorsAsync(response, cancellationToken);
        var detail = providerErrors.Count > 0
            ? string.Join("; ", providerErrors)
            : $"The billing provider responded with HTTP {status}.";

        throw status switch
        {
            401 or 403 => new BillingAuthenticationException(
                "The billing provider rejected the configured credentials.", status, providerErrors),
            404 => new BillingEntityNotFoundException(detail, providerErrors),
            400 or 422 => new BillingValidationException(detail, status, providerErrors),
            408 or 429 or >= 500 => new BillingProviderUnavailableException(
                $"The billing provider is unavailable (HTTP {status}).", status, providerErrors),
            _ => new BillingProviderException(detail, status, providerErrors)
        };
    }

    /// <summary>
    /// Reads the provider's error payload, which the specification models either as
    /// <c>{"errors":["..."]}</c> or as <c>{"errors":{"field":"..."}}</c>.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadProviderErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors))
            {
                switch (errors.ValueKind)
                {
                    case JsonValueKind.Array:
                        return errors.EnumerateArray()
                            .Select(e => e.ToString())
                            .Where(e => !string.IsNullOrWhiteSpace(e))
                            .ToList();
                    case JsonValueKind.Object:
                        return errors.EnumerateObject()
                            .Select(p => $"{p.Name}: {p.Value}")
                            .ToList();
                    case JsonValueKind.String:
                        return new[] { errors.GetString()! };
                }
            }
        }
        catch (JsonException)
        {
            // Not a JSON error envelope - fall through and use the raw body.
        }

        return new[] { Truncate(body) };
    }

    private static string Truncate(string value)
    {
        var collapsed = value.Trim();
        return collapsed.Length <= MaxProviderMessageLength
            ? collapsed
            : collapsed[..MaxProviderMessageLength] + "...";
    }

    private static BillingPlan ToPlan(MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Price = FromCents(product.PriceInCents),
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        ProductFamilyId = product.ProductFamily?.Id ?? 0,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        RequiresPaymentMethod = product.RequireCreditCard,
        Archived = product.ArchivedAt is not null
    };

    private static BillingCustomer ToCustomer(MaxioCustomer customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        Email = customer.Email,
        FirstName = customer.FirstName,
        LastName = customer.LastName
    };

    private static BillingSubscription ToSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        CustomerEmail = subscription.Customer?.Email,
        ProductId = subscription.Product?.Id ?? 0,
        ProductHandle = subscription.Product?.Handle,
        ProductName = subscription.Product?.Name,
        ProductPriceInCents = subscription.ProductPriceInCents,
        ProductPrice = FromCents(subscription.ProductPriceInCents),
        BalanceInCents = subscription.BalanceInCents,
        Balance = FromCents(subscription.BalanceInCents),
        Currency = string.IsNullOrWhiteSpace(subscription.Currency) ? "USD" : subscription.Currency,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        DelayedCancelAt = subscription.DelayedCancelAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? false
    };

    private static BillingComponent ToComponent(MaxioComponent component) => new()
    {
        Id = component.Id,
        Handle = component.Handle,
        Name = component.Name ?? string.Empty,
        Kind = component.Kind ?? string.Empty,
        PricingScheme = component.PricingScheme,
        UnitPrice = ParseUnitPrice(component),
        UnitName = component.UnitName,
        ProductFamilyId = component.ProductFamilyId,
        ProductFamilyHandle = component.ProductFamilyHandle,
        Archived = component.Archived
    };

    /// <summary>
    /// The provider reports a component's unit price as a decimal string in the site currency
    /// (e.g. "0.01"), and only for per-unit schemes.
    /// </summary>
    private static decimal? ParseUnitPrice(MaxioComponent component)
    {
        if (string.IsNullOrWhiteSpace(component.UnitPrice))
        {
            return null;
        }

        return decimal.TryParse(component.UnitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
            ? price
            : null;
    }

    private static decimal ReadQuantity(JsonElement quantity, decimal requested) => quantity.ValueKind switch
    {
        JsonValueKind.Number => quantity.GetDecimal(),
        JsonValueKind.String when decimal.TryParse(quantity.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => requested
    };

    /// <summary>
    /// The provider requires a first and last name on a customer, but eShopOnWeb only knows the
    /// signed-in user's email / username, so the local part is split on the usual separators.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(string email, string userReference)
    {
        var source = string.IsNullOrWhiteSpace(email) ? userReference : email;
        var localPart = source.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => (userReference, userReference),
            1 => (parts[0], userReference),
            _ => (parts[0], string.Join(' ', parts.Skip(1)))
        };
    }

    /// <summary>Converts the provider's integer minor units into the site currency.</summary>
    private static decimal FromCents(long cents) => cents / 100m;

    private static string RequireHandle(string? value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new BillingConfigurationException($"'{name}' is required but was not supplied.")
            : value;
}
