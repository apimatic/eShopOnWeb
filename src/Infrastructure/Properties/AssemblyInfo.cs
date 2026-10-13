using System.Runtime.CompilerServices;

// Allow the unit test project to exercise the internal Walmart integration types
// (URL resolver, inventory service, contract constants) directly.
[assembly: InternalsVisibleTo("UnitTests")]
