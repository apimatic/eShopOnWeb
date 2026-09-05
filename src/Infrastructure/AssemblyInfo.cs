using System.Runtime.CompilerServices;

// Lets UnitTests substitute the internal Maxio client/contracts when testing
// MaxioSubscriptionService's idempotency logic in isolation from the network.
[assembly: InternalsVisibleTo("UnitTests")]

// NSubstitute proxies internal interfaces via Castle DynamicProxy's dynamic assembly, which
// needs its own visibility grant to implement IMaxioClient.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
