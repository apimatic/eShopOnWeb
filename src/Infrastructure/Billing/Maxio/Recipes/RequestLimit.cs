// RequestLimit.cs
//
// Lets at most four calls to one Maxio site run at a time in this process, first come first
// served. Wrap a call in Run and name the subdomain it goes to. A caller that is waiting for a
// slot can be cancelled. WithoutUnsafeRetries returns the client's retry settings without the two
// automatic resends that Maxio's rules forbid.
//
// Implements: knowledge/rules.md R10 (four requests in flight per subdomain) and the automatic
// resend part of R11.
//
// What "in flight" means here. Read this before you rely on the number four.
// - Slots are counted in this process. Maxio counts at its own end. The two agree only while your
//   calls end normally.
// - A slot is taken when your call starts and given back when the task it returned completes or
//   fails. The SDK's own resends and the waits between them happen inside the call, so they stay
//   in that one slot.
// - After a client timeout or a cancellation the slot is given back at once, although Maxio may go
//   on working on that request for up to 120 seconds. After timeouts Maxio can be working on more
//   than four of your requests while this file counts four or fewer.
// - Run counts calls, not requests. Make one request at a time inside a call. A call that starts
//   several requests at once, or leaves one running after it returns, has more than one request
//   in its one slot.
// - One process only. Several instances of your app share Maxio's four and share nothing here.
//
// Nested use for one site is safe. A Run inside a Run for the same site takes no second slot. It
// runs in the slot its caller holds, and the nested calls of one caller take turns. So you can
// wrap a whole operation and also each SDK call inside it, and four callers cannot end up waiting
// on each other. The caller is found along the async flow (AsyncLocal). Code that suppresses the
// flow of ExecutionContext is not found and takes a slot of its own. A Run for another site
// takes a slot of that site, nested or not, so do not nest two sites in both orders: callers
// that each hold one site and wait for the other can still block each other.
//
// The SDK's own resends. Both settings are on MaxioAdvancedBillingClientOptions.Retry, are fixed
// when the client is built, and apply to every call that client makes. This SDK has no retry
// setting for one call, so send writes through a client built without these resends
// (docs/configuration-resilience.md, "Maxio: retries, timeouts and paging").
// - Retry.StatusCodesToRetry holds 429 by default. The SDK then resends a throttled GET, HEAD, PUT
//   or OPTIONS up to 3 times, after about 1, 2 and 4 seconds. Maxio asks for a pause of minutes.
//   Take 429 out and the first 429 is thrown to you as an ApiException.
// - Retry.HttpMethodsToRetry holds PUT by default. The SDK then resends a PUT after a 408, 429,
//   500, 502, 503 or 504, after a timeout and after a connection failure. RetrySubscription,
//   ReactivateSubscription and ActivateSubscription are PUTs that can take a payment. Take PUT out
//   and a PUT is sent once. POST and DELETE are not in the default list and are sent once.
// WithoutUnsafeRetries makes both changes. It takes 429 out, and it leaves only GET, HEAD and
// OPTIONS to be resent:
//     options.Retry = RequestLimit.WithoutUnsafeRetries(options.Retry);
//
// What it does not do:
// - It does not pause after a 429 or restart slower. That is yours to write (R10).
// - It does not repeat a write, and it does not decide whether one may be repeated (R11, R12).
// - It does not raise the client's timeouts. recipes/SafeSignup.cs says what a write needs.
//
// Copy into your project and adapt. Not part of the SDK's API.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Configuration;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Recipes;

/// <summary>Keeps this process within Maxio's limit of four requests in flight per site (knowledge/rules.md R10).</summary>
public static class RequestLimit
{
    /// <summary>Maxio's limit for one subdomain.</summary>
    public const int MaxInFlight = 4;

    // One gate per subdomain for the whole process, because Maxio counts per site, not per client
    // object. A gate created per request or per client would limit nothing.
    private static readonly ConcurrentDictionary<string, Gate> Sites =
        new ConcurrentDictionary<string, Gate>(StringComparer.OrdinalIgnoreCase);

    // The call this async flow is inside, if any. It is how a nested Run finds its caller's slot.
    private static readonly AsyncLocal<Held?> Inside = new AsyncLocal<Held?>();

    /// <summary>
    /// Runs <paramref name="call"/> in one of the four slots of <paramref name="subdomain"/> and
    /// returns its result. Waits, first come first served, while all four are taken. Inside another
    /// Run for the same site it takes no second slot and waits only for its caller's other nested
    /// calls.
    /// </summary>
    /// <param name="subdomain">The site the call goes to, as in <c>options.Server.Production.Us.Site</c>. Case does not matter.</param>
    /// <param name="call">One SDK call, or a sequence of them made one at a time.</param>
    /// <param name="cancellationToken">Ends the wait for a slot. It does not reach <paramref name="call"/>: pass it there yourself.</param>
    public static async Task<T> Run<T>(
        string subdomain, Func<Task<T>> call, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subdomain))
            throw new ArgumentException("Name the subdomain the call goes to.", nameof(subdomain));
        _ = call ?? throw new ArgumentNullException(nameof(call));

        // Nested: a second slot is what lets four callers each hold one and wait for another
        // forever. So a call made inside a call that holds a slot of this site queues on that
        // call's own gate of one. The search goes up through calls for other sites in between.
        var caller = Inside.Value;
        var holder = caller;
        while (holder != null && !(holder.Running && holder.IsFor(subdomain)))
            holder = holder.Caller;
        var gate = holder != null ? holder.Turns : Sites.GetOrAdd(subdomain, _ => new Gate(MaxInFlight));

        await gate.Enter(cancellationToken).ConfigureAwait(false);
        var held = new Held(subdomain, caller);
        Inside.Value = held;
        try
        {
            return await call().ConfigureAwait(false);
        }
        finally
        {
            held.Running = false;
            Inside.Value = caller;
            gate.Leave();
        }
    }

    /// <summary>The same as <see cref="Run{T}"/> for a call that returns nothing.</summary>
    /// <param name="subdomain">The site the call goes to. Case does not matter.</param>
    /// <param name="call">One SDK call, or a sequence of them made one at a time.</param>
    /// <param name="cancellationToken">Ends the wait for a slot. It does not reach <paramref name="call"/>.</param>
    public static Task Run(
        string subdomain, Func<Task> call, CancellationToken cancellationToken = default)
    {
        _ = call ?? throw new ArgumentNullException(nameof(call));

        return Run(
            subdomain,
            async () =>
            {
                await call().ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// <paramref name="retry"/> without the automatic resends Maxio's rules forbid. 429 is taken
    /// out of <see cref="RetryOptions.StatusCodesToRetry"/>, and every method except GET, HEAD and
    /// OPTIONS is taken out of <see cref="RetryOptions.HttpMethodsToRetry"/>, which removes PUT.
    /// Everything else is kept. Assign the result to
    /// <c>MaxioAdvancedBillingClientOptions.Retry</c> before you build the client.
    /// </summary>
    public static RetryOptions WithoutUnsafeRetries(RetryOptions retry)
    {
        _ = retry ?? throw new ArgumentNullException(nameof(retry));

        return retry with
        {
            StatusCodesToRetry = retry.StatusCodesToRetry.Where(status => (int)status != 429).ToArray(),
            HttpMethodsToRetry = retry.HttpMethodsToRetry.Where(IsRead).ToArray(),
        };
    }

    private static bool IsRead(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Options;

    // A call that holds a slot. Nested calls for the same site wait on Turns, one at a time.
    private sealed class Held
    {
        private readonly string _subdomain;
        private Gate? _turns;

        public Held(string subdomain, Held? caller)
        {
            _subdomain = subdomain;
            Caller = caller;
            Running = true;
        }

        // The call this one was made inside, if any.
        public Held? Caller { get; }

        // False once the call has returned. A task that outlives the call must not run in a slot
        // that was already given back, so it then takes one of its own.
        public volatile bool Running;

        public Gate Turns => LazyInitializer.EnsureInitialized(ref _turns, () => new Gate(1))!;

        public bool IsFor(string subdomain) =>
            string.Equals(_subdomain, subdomain, StringComparison.OrdinalIgnoreCase);
    }

    // A counted gate that serves waiters strictly in the order they arrived. SemaphoreSlim does
    // not promise an order.
    private sealed class Gate
    {
        private readonly LinkedList<TaskCompletionSource<bool>> _waiting =
            new LinkedList<TaskCompletionSource<bool>>();

        private int _free;

        public Gate(int slots)
        {
            _free = slots;
        }

        public Task Enter(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            TaskCompletionSource<bool> waiter;
            LinkedListNode<TaskCompletionSource<bool>> place;
            lock (_waiting)
            {
                // A slot is free only when nobody waits, so taking it here overtakes no one.
                if (_free > 0)
                {
                    _free--;
                    return Task.CompletedTask;
                }

                waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                place = _waiting.AddLast(waiter);
            }

            return cancellationToken.CanBeCanceled ? Wait(waiter, place, cancellationToken) : waiter.Task;
        }

        public void Leave()
        {
            TaskCompletionSource<bool>? next = null;
            lock (_waiting)
            {
                // The slot goes straight to the caller who has waited longest.
                var first = _waiting.First;
                if (first != null)
                {
                    next = first.Value;
                    _waiting.RemoveFirst();
                }
                else
                {
                    _free++;
                }
            }

            next?.TrySetResult(true);
        }

        private async Task Wait(
            TaskCompletionSource<bool> waiter,
            LinkedListNode<TaskCompletionSource<bool>> place,
            CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(() => Withdraw(waiter, place, cancellationToken)))
            {
                await waiter.Task.ConfigureAwait(false);
            }
        }

        // Runs when the waiter's token is cancelled. If Leave has already handed this waiter a
        // slot, the waiter keeps it and its call starts: a slot is never lost.
        private void Withdraw(
            TaskCompletionSource<bool> waiter,
            LinkedListNode<TaskCompletionSource<bool>> place,
            CancellationToken cancellationToken)
        {
            bool waiting;
            lock (_waiting)
            {
                waiting = place.List != null;
                if (waiting)
                    _waiting.Remove(place);
            }

            if (waiting)
                waiter.TrySetCanceled(cancellationToken);
        }
    }
}
