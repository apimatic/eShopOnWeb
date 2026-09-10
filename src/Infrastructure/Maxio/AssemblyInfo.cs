using System.Runtime.CompilerServices;

// Expose the internal Maxio client/service surface to the unit test assembly so the idempotent
// orchestration can be tested directly without going over the network.
[assembly: InternalsVisibleTo("UnitTests")]
