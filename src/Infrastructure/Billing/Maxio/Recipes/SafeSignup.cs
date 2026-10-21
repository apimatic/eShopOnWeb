// SafeSignup.cs
//
// Signs one of your users up without creating a second subscription, and tells you which of four
// things happened. It needs recipes/SubscriptionState.cs beside it.
//
// Implements: knowledge/rules.md R11, R12, R14; knowledge/lifecycles.md L2 (FindCustomer is step
// 4, FindHeldSubscription the part of step 6 about the customer's other subscriptions; the
// subscribe and settle steps) and L3.
//
// What one call does, in order:
// 1. Looks the subscription up by your reference. Found: returns Existing. No create is sent.
// 2. Sends the create once, with your uniqueness_token at the top level of the body, beside
//    "subscription". The create is never sent twice by one call, even when the client's retry
//    settings would resend it.
// 3. A 2xx with a subscription in it: returns Created.
// 4. A 4xx other than 408 and 409: Maxio rejected the request. The SDK's exception is thrown as
//    it is.
// 5. A 409: Maxio has received this token before, so an earlier copy of this signup arrived. That
//    proves delivery, not success. Reads back by reference. Found: Recovered. Not found: Unknown
//    with FirstCopyArrived true.
// 6. A timeout, a dropped connection, a 5xx, a 408 or a 2xx that cannot be read: the outcome is
//    unknown. Reads back by reference. Found: Recovered. Not found: Unknown with FirstCopyArrived
//    false.
// 7. The read-back fails as well: Unknown, carrying both failures.
// It does not resend. Calling it again with the same arguments is the resend.
//
// The outcome is unknown, as in docs/configuration-resilience.md ("Maxio: retries, timeouts and
// paging"), after an ApiException with status 408, 409, 5xx or 2xx (an unreadable 2xx arrives as
// ResponseDeserializationException), after an SdkConnectionException (SdkTimeoutException is one),
// and after your own cancellation once the create was sent. SignUp throws when nothing was sent
// (an argument was wrong, the lookup in step 1 failed, your token was cancelled before the create
// went out), when Maxio rejected the create (step 4), and for an exception that is none of the
// above, such as a 3xx answer, which it throws as it is. Every unknown outcome is a returned
// SignupResult. Branch on it.
//
// What you pass:
// - reference: the subscription reference, derived from your own record of this signup (an order
//   or signup id). Every call for this signup passes the same one. Never random.
// - uniquenessToken: mint one long random value (a GUID will do) once per signup, store it with
//   your own record of that signup, and pass the same value on every call for it. Mint a new one
//   only after Maxio rejected the request (a 4xx other than 409 or 429) and you corrected it: a
//   received request spends its token even when it is rejected. Or mint one once the first attempt
//   is known to have failed (see Unknown below). This file never mints one.
// - paymentCollectionMethod: required, with no default. Left out, Maxio applies the site's own
//   default, and where that is "automatic" a signup that owes money and has no payment source is
//   rejected with a 422 that does not name the field (R14). Choose it by how the customer pays
//   (knowledge/decisions.md D3).
// - subscription: the rest of the create body. Exactly one customer source (knowledge/decisions.md
//   D1), the product, at most one payment source (D2). Its Reference and
//   PaymentCollectionMethod are replaced by the two arguments above.
//
// What to do with each result:
// - Created: this call created it. Record it.
// - Existing: it was there before this call sent anything. Look at Standing before you call the
//   signup done.
// - Recovered: the create's outcome was unknown and the subscription was then found by
//   reference. Look at Standing.
// - Unknown: it may or may not exist. This is not a failure. Keep your signup record open, wait
//   until 120 seconds have passed since you first sent the create (Maxio may still be working on
//   it), and call SignUp again with the same reference and the same token. If the earlier create
//   has landed by then, the lookup finds it: Existing. Otherwise the create is sent again with the
//   same token. A 201 means the first copy never arrived and this one was processed: Created. A
//   409 means it did arrive: Recovered, or Unknown with FirstCopyArrived true while nothing has
//   the reference. Only that last case, after the 120 seconds, means the attempt failed. Only
//   then mint a new token and start again (knowledge/rules.md R11).
//
// What Standing means for a signup:
// - Live or Transient: the signup landed. Stop.
// - Recoverable: on hold, suspended, unpaid or awaiting signup. Not billing, and it comes back.
//   Stop. Never create a second subscription beside it.
// - Ended: canceled, expired, trial ended or failed to create. This is not a successful signup,
//   although a subscription was returned. Reactivate it, or sign up again under a new reference
//   and a new token, as your user intends.
// - Unclassified: a state this SDK does not know. Stop and read what the state means.
//
// Timeouts and cancellation:
// - The client must wait at least 130 seconds for an answer, so that Maxio's own cut-off at 120
//   seconds answers first. Each of the SDK's two timeouts defaults to 100 seconds, the shorter
//   one wins, and the SDK has no timeout for a single call, so set both on the client you pass:
//       options.Retry = options.Retry with { Timeout = SafeSignup.MinimumClientTimeout };
//       httpClient.Timeout = SafeSignup.MinimumClientTimeout;
//   docs/configuration-resilience.md, "Maxio: retries, timeouts and paging", shows a whole client
//   built for writes.
// - Do not give this call a deadline under 120 seconds, and do not pass or link a request-aborted
//   token (HttpContext.RequestAborted) to it. A browser that disconnects would abandon a create
//   that Maxio then completes.
// - Once the create has been sent, your CancellationToken no longer makes SignUp throw. If it is
//   cancelled, or its deadline fires, the create is abandoned (Maxio may still finish it) and the
//   read-back runs on a timer of its own, 30 seconds unless you pass readBackTimeout. SignUp then
//   returns Recovered or Unknown.
//
// Reading a rejection: a 422 is thrown as ApiException<CreateSubscriptionError>. It can be a
// 3-D Secure challenge, which no change to the request fixes. No SDK model declares action_link,
// so read it from the error's undeclared fields:
//     ex.Error.TryGetErrorListResponse1(out var errors) &&
//         errors.AdditionalProperties.TryGetValue<string>("action_link", out var actionLink)
// Send the customer to the link. Maxio does not say whether a subscription exists at that point,
// so look the reference up afterwards before you send anything new (docs/models.md, "Fields the
// spec does not declare"). A 429 is thrown too and is not a fault in the request: pause for
// minutes (knowledge/rules.md R10), then call again with the same token.
//
// What it does not do:
// - It does not serialize signups. Two calls for one customer at the same time both miss the
//   lookup and both send a create, and the token does not reliably stop the second one: one test
//   run got duplicate subscriptions from requests sent at the same moment. Hold a lock per
//   customer in your own store around this call. A lock inside one process is not enough when you
//   run more than one instance.
// - It does not look the customer up by itself. Call FindCustomer (below) before every signup and
//   name the customer by what it answers: customer_attributes for a customer who already exists
//   creates a second customer (knowledge/decisions.md D1). When it finds one, call
//   FindHeldSubscription (below) too: SignUp looks only at its own reference, and a customer who
//   already holds the plan under another reference gets a second subscription beside it.
// - It does not choose the collection method.
// - It does not wait, poll or resend.
// - It does not go through recipes/RequestLimit.cs. Wrap the call in RequestLimit.Run. Its
//   requests are made one at a time, so one slot covers them.
//
// Copy into your project and adapt. Not part of the SDK's API.

#nullable enable

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.Hooks;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Recipes;

/// <summary>Creates a subscription at most once and settles an unknown outcome by reading back.</summary>
public static class SafeSignup
{
    /// <summary>
    /// The shortest time the client may wait for an answer to a write: above Maxio's own cut-off
    /// at 120 seconds. Use it for <c>options.Retry.Timeout</c> and for <c>HttpClient.Timeout</c>.
    /// </summary>
    public static readonly TimeSpan MinimumClientTimeout = TimeSpan.FromSeconds(130);

    /// <summary>How long the read-back after an unknown outcome may take when you pass no other value.</summary>
    public static readonly TimeSpan DefaultReadBackTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Signs up once. Returns what happened as a <see cref="SignupResult"/>. Throws only when
    /// nothing was sent (a wrong argument, a failed lookup before the create, a token cancelled
    /// before the create went out) or when Maxio rejected the create (a 4xx other than 408 and
    /// 409, thrown as the SDK's own exception).
    /// </summary>
    /// <param name="client">A client that waits at least <see cref="MinimumClientTimeout"/> for an answer.</param>
    /// <param name="subscription">The create body: one customer source, the product, at most one payment source.</param>
    /// <param name="reference">Your deterministic reference for this signup. It replaces <c>subscription.Reference</c>.</param>
    /// <param name="uniquenessToken">The token you minted once for this signup and stored. The same value on every call for it.</param>
    /// <param name="paymentCollectionMethod">How the customer pays. It replaces <c>subscription.PaymentCollectionMethod</c>.</param>
    /// <param name="readBackTimeout">The longest the read-back after an unknown outcome may take. 30 seconds when null.</param>
    /// <param name="cancellationToken">
    /// Ends the call only until the create is sent. No deadline under 120 seconds, and never a
    /// request-aborted token.
    /// </param>
    public static async Task<SignupResult> SignUp(
        MaxioAdvancedBillingClient client,
        CreateSubscription subscription,
        string reference,
        string uniquenessToken,
        CollectionMethod paymentCollectionMethod,
        TimeSpan? readBackTimeout = null,
        CancellationToken cancellationToken = default)
    {
        _ = client ?? throw new ArgumentNullException(nameof(client));
        _ = subscription ?? throw new ArgumentNullException(nameof(subscription));
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("A signup needs a reference that it can be looked up by.", nameof(reference));
        if (string.IsNullOrWhiteSpace(uniquenessToken))
            throw new ArgumentException("Pass the uniqueness token you stored for this signup.", nameof(uniquenessToken));
        _ = paymentCollectionMethod ?? throw new ArgumentNullException(nameof(paymentCollectionMethod));

        var readBackWait = readBackTimeout ?? DefaultReadBackTimeout;
        if (readBackWait <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(readBackTimeout), readBackTimeout, "The read-back needs a positive time limit.");

        // Step 1. An earlier call for this signup may have landed without your learning of it.
        var existing = await Find(client, reference, cancellationToken).ConfigureAwait(false);
        if (existing != null)
            return new SignupResult.Existing(existing);

        var body = new CreateSubscriptionRequest
        {
            Subscription = subscription with
            {
                Reference = reference,
                PaymentCollectionMethod = paymentCollectionMethod,
            },
        };

        // No request model declares uniqueness_token. The root model's undeclared fields are
        // written beside "subscription", which is where Maxio reads the token. Nested inside
        // "subscription" it is ignored. The Idempotency-Key header the SDK adds is not Maxio's
        // mechanism.
        body.AdditionalProperties.Set("uniqueness_token", uniquenessToken);

        // Step 2. The hook counts sends, so "was the create sent" is a fact and not a guess, and it
        // refuses a second send whatever the client's retry settings say.
        var once = new SendOnce();
        Exception createFailure;
        try
        {
            var answer = await client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest { Body = body },
                new RequestOptions { Hooks = new SdkHook[] { once } },
                cancellationToken).ConfigureAwait(false);

            // Step 3.
            if (answer?.Subscription is { } created)
                return new SignupResult.Created(created);

            createFailure = new InvalidOperationException(
                "Maxio answered the create with a 2xx that carries no subscription.");
        }
        catch (Exception failure) when (once.Sent && LeavesOutcomeUnknown(failure, once))
        {
            // Steps 5 and 6. Not caught here, and so thrown as they are: a rejection (step 4), any
            // failure from before the create was sent, and any exception that is not an answer, a
            // connection fault or a cancellation.
            createFailure = failure;
        }

        // A 409 is Maxio saying it has seen this token before.
        var firstCopyArrived = once.Answer == HttpStatusCode.Conflict;

        // The read-back must not depend on the caller's token: that token may be the very thing
        // that ended the create, and a read-back that is cancelled with it settles nothing.
        Subscription? found = null;
        Exception? readBackFailure = null;
        using (var limit = new CancellationTokenSource(readBackWait))
        {
            try
            {
                found = await Find(client, reference, limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException timedOut) when (limit.IsCancellationRequested)
            {
                readBackFailure = new TimeoutException(
                    "The read-back by reference did not finish within " + readBackWait + ".", timedOut);
            }
            catch (Exception failure)
            {
                // Step 7. The read-back's failure must never replace the unknown outcome.
                readBackFailure = failure;
            }
        }

        return found != null
            ? new SignupResult.Recovered(found, createFailure)
            : new SignupResult.Unknown(firstCopyArrived, createFailure, readBackFailure);
    }

    /// <summary>
    /// The customer that has <paramref name="reference"/>, your own user id, or null when Maxio
    /// answers 404 (knowledge/lifecycles.md L2 step 4). Call it before every signup, one for a "new"
    /// customer too: a resend after a lost answer meets the customer the first attempt created.
    /// Found: pass it, with the signup's reference, to <see cref="FindHeldSubscription"/> before you
    /// sign it up again (L2 step 6), then name the customer with <c>CustomerReference = reference</c>.
    /// Null: send
    /// <c>CustomerAttributes</c> with the reference in it, so the new customer has it. It sends no write.
    /// </summary>
    /// <exception cref="ApiException{RawError}">Maxio answered with any failure other than 404.</exception>
    public static async Task<Customer?> FindCustomer(
        MaxioAdvancedBillingClient client, string reference, CancellationToken cancellationToken = default)
    {
        _ = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("A customer is looked up by your own user id; pass it.", nameof(reference));
        try
        {
            var answer = await client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return answer?.Customer ?? throw new InvalidOperationException(
                "Maxio answered the customer lookup with a 2xx that carries no customer.");
        }
        catch (ApiException<RawError> notThere) when (notThere.StatusCode == HttpStatusCode.NotFound)
        {
            // Only a 404 is "not there". Any other failure stays an exception: "could not look it up"
            // read as "does not exist" turns an outage into a second customer.
            return null;
        }
    }

    /// <summary>The most pages of one customer's subscriptions <see cref="FindHeldSubscription"/> reads.</summary>
    public const int HeldMaxPages = 50;

    /// <summary>
    /// A subscription that <paramref name="customer"/> already holds on <paramref name="productHandle"/>
    /// and that leaves no room for a second one beside it, or null (knowledge/lifecycles.md L2 step 6
    /// and the fourth column of L3; knowledge/rules.md R16). Call it with what
    /// <see cref="FindCustomer"/> found, before you sign that customer up again: a customer the task
    /// calls "new" can be one who already holds the plan. Pass this signup's own reference too. A
    /// subscription carrying it is this signup's earlier attempt, not a second plan: it is skipped here,
    /// and <see cref="SignUp"/> finds it by that reference and returns Existing.
    /// Found: do not create. Its <c>State.Standing()</c> (recipes/SubscriptionState.cs) says what to
    /// tell the user: Live, the customer already has the plan; Transient, Maxio is still working on
    /// it, so read it again; Recoverable, resume it or collect on it; Unclassified, decide by hand.
    /// Null: nothing on this plan blocks a signup. An Ended one (canceled, expired, trial ended,
    /// failed to create) does not: sign up again, or bring a canceled or trial-ended one back with a
    /// reactivation if that is what the user wants (knowledge/decisions.md D12).
    /// It pages <c>ListSubscriptions</c> filtered by <c>CustomerId</c>, 200 a page in id order, until
    /// a page comes back empty (knowledge/rules.md R8); <c>ListCustomerSubscriptions</c> declares no
    /// paging. It matches the product by <c>Product.Handle</c>, so a subscription with no product
    /// (new-Catalog sites) is not matched. Any failure is thrown, never read as "nothing held", and so
    /// is a row with no subscription in it. It sends no write.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A row had no subscription in it, or the customer has more than <see cref="HeldMaxPages"/> pages
    /// of subscriptions.
    /// </exception>
    public static async Task<Subscription?> FindHeldSubscription(
        MaxioAdvancedBillingClient client,
        Customer customer,
        string productHandle,
        string signupReference,
        CancellationToken cancellationToken = default)
    {
        _ = client ?? throw new ArgumentNullException(nameof(client));
        _ = customer ?? throw new ArgumentNullException(nameof(customer));
        if (customer.Id is not int customerId || customerId <= 0)
            throw new ArgumentException("The customer has no id; pass what FindCustomer returned.", nameof(customer));
        if (string.IsNullOrWhiteSpace(productHandle))
            throw new ArgumentException("Pass the plan's product handle.", nameof(productHandle));
        if (string.IsNullOrWhiteSpace(signupReference))
            throw new ArgumentException("Pass this signup's reference.", nameof(signupReference));

        for (var page = 1; page <= HeldMaxPages; page++)
        {
            var rows = await client.Subscriptions.ListSubscriptions(
                new ListSubscriptionsRequest
                {
                    CustomerId = customerId,
                    Page = page,
                    PerPage = 200,
                    Sort = SubscriptionSort.Id,
                    Direction = SortingDirection.Asc,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0) return null; // the only end signal

            foreach (var row in rows)
            {
                var subscription = row.Subscription ?? throw new InvalidOperationException(
                    "Maxio answered the subscription list with a row that has no subscription in it.");
                if (subscription.Product?.Handle != productHandle) continue;
                if (subscription.Reference == signupReference) continue; // this signup itself: SignUp reports it
                if (subscription.State.Standing() != SubscriptionStanding.Ended) return subscription;
            }
        }

        throw new InvalidOperationException(
            $"Customer {customerId} has more than {HeldMaxPages} pages of subscriptions; check by hand.");
    }

    // The subscription that has the reference, or null when Maxio answers 404, which is its
    // normal "not there" answer. Every other failure stays an exception: reading "could not look
    // it up" as "does not exist" turns an outage into a second subscription.
    private static async Task<Subscription?> Find(
        MaxioAdvancedBillingClient client, string reference, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // A 2xx with no subscription in it is not a miss either: null here reads as "safe to
            // create".
            return answer?.Subscription ?? throw new InvalidOperationException(
                "Maxio answered the lookup by reference with a 2xx that carries no subscription.");
        }
        catch (ApiException<FindSubscriptionError> notThere) when (notThere.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // A write whose outcome is unknown. Every other 4xx is a definite "no" from Maxio.
    private static bool LeavesOutcomeUnknown(Exception failure, SendOnce once)
    {
        switch (failure)
        {
            case ApiException answered:
                var status = (int)answered.StatusCode;
                // 408: the request timed out on the way. 409: an earlier copy arrived. A 2xx here
                // is a ResponseDeserializationException: Maxio accepted and the body was unreadable.
                return status == 408 || status == 409 || (status >= 500 && status < 600) || (status >= 200 && status < 300);
            case SdkConnectionException _:
            case OperationCanceledException _:
                return true;
            default:
                // The client's retry settings tried to send the create a second time and SendOnce
                // stopped it. The first copy was sent and its answer was one the client retries.
                return once.StoppedResend;
        }
    }

    // Passed to the create as a per-call hook. The SDK runs BeforeRequest before every send, its
    // own resends included, and AfterResponse after every answer.
    private sealed class SendOnce : SdkHook
    {
        private int _sends;
        private int _answer;

        public bool Sent => Volatile.Read(ref _sends) > 0;

        public bool StoppedResend => Volatile.Read(ref _sends) > 1;

        public HttpStatusCode? Answer
        {
            get
            {
                var status = Volatile.Read(ref _answer);
                return status == 0 ? (HttpStatusCode?)null : (HttpStatusCode)status;
            }
        }

        public override ValueTask BeforeRequest(
            HttpRequestMessage request, HookContext context, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _sends) > 1)
            {
                throw new InvalidOperationException(Answer is { } status
                    ? "Maxio answered the create with " + (int)status + " and the client's retry settings tried to send it again. The second send was stopped."
                    : "The create got no answer and the client's retry settings tried to send it again. The second send was stopped.");
            }

            return default;
        }

        public override ValueTask AfterResponse(
            HttpResponseMessage response, HookContext context, CancellationToken cancellationToken)
        {
            Volatile.Write(ref _answer, (int)response.StatusCode);
            return default;
        }
    }
}

/// <summary>
/// What <see cref="SafeSignup.SignUp"/> did. It is one of <see cref="Created"/>,
/// <see cref="Existing"/>, <see cref="Recovered"/> and <see cref="Unknown"/>. There is no
/// subscription to read until you have said which: use <see cref="Match{TResult}"/>, which makes
/// you handle all four, or a type pattern.
/// </summary>
public abstract class SignupResult
{
    private SignupResult()
    {
    }

    /// <summary>Calls the function for the case this result is and returns what it returns.</summary>
    public TResult Match<TResult>(
        Func<Created, TResult> created,
        Func<Existing, TResult> existing,
        Func<Recovered, TResult> recovered,
        Func<Unknown, TResult> unknown)
    {
        _ = created ?? throw new ArgumentNullException(nameof(created));
        _ = existing ?? throw new ArgumentNullException(nameof(existing));
        _ = recovered ?? throw new ArgumentNullException(nameof(recovered));
        _ = unknown ?? throw new ArgumentNullException(nameof(unknown));

        switch (this)
        {
            case Created itWasCreated:
                return created(itWasCreated);
            case Existing itExisted:
                return existing(itExisted);
            case Recovered itWasRecovered:
                return recovered(itWasRecovered);
            default:
                return unknown((Unknown)this);
        }
    }

    /// <summary>This call created the subscription.</summary>
    public sealed class Created : SignupResult
    {
        /// <summary>Creates the result for a subscription this call created.</summary>
        public Created(Subscription subscription)
        {
            Subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
        }

        /// <summary>The subscription Maxio returned from the create.</summary>
        public Subscription Subscription { get; }
    }

    /// <summary>
    /// A subscription already had the reference before this call sent anything. No create was
    /// sent. It is a successful signup only when <see cref="Standing"/> says so.
    /// </summary>
    public sealed class Existing : SignupResult
    {
        /// <summary>Creates the result for a subscription found before any create was sent.</summary>
        public Existing(Subscription subscription)
        {
            Subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
            Standing = subscription.State.Standing();
        }

        /// <summary>The subscription that has the reference.</summary>
        public Subscription Subscription { get; }

        /// <summary>Where that subscription stands. An ended one is not a successful signup.</summary>
        public SubscriptionStanding Standing { get; }
    }

    /// <summary>
    /// The create's outcome was unknown, and the subscription was then found by reference. It is
    /// a successful signup only when <see cref="Standing"/> says so.
    /// </summary>
    public sealed class Recovered : SignupResult
    {
        /// <summary>Creates the result for a subscription found by the read-back.</summary>
        public Recovered(Subscription subscription, Exception createFailure)
        {
            Subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
            CreateFailure = createFailure ?? throw new ArgumentNullException(nameof(createFailure));
            Standing = subscription.State.Standing();
        }

        /// <summary>The subscription that has the reference.</summary>
        public Subscription Subscription { get; }

        /// <summary>Where that subscription stands. An ended one is not a successful signup.</summary>
        public SubscriptionStanding Standing { get; }

        /// <summary>Why the create's outcome was unknown.</summary>
        public Exception CreateFailure { get; }
    }

    /// <summary>
    /// The subscription may or may not exist. Not a failure. Wait and call
    /// <see cref="SafeSignup.SignUp"/> again with the same reference and the same token.
    /// </summary>
    public sealed class Unknown : SignupResult
    {
        /// <summary>Creates the result for a create whose outcome could not be settled.</summary>
        public Unknown(bool firstCopyArrived, Exception createFailure, Exception? readBackFailure)
        {
            FirstCopyArrived = firstCopyArrived;
            CreateFailure = createFailure ?? throw new ArgumentNullException(nameof(createFailure));
            ReadBackFailure = readBackFailure;
        }

        /// <summary>
        /// True when Maxio answered 409: it has received this token before, so an earlier copy of
        /// this signup arrived, and nothing has the reference yet. False when it is not known
        /// whether any copy arrived.
        /// </summary>
        public bool FirstCopyArrived { get; }

        /// <summary>Why the create's outcome is unknown.</summary>
        public Exception CreateFailure { get; }

        /// <summary>
        /// Why the read-back could not answer, or null when it ran and nothing had the reference.
        /// </summary>
        public Exception? ReadBackFailure { get; }
    }
}
