// Resolve HotChocolate.Path vs System.IO.Path ambiguity introduced when
// VA.CMS.Tests references HotChocolate.AspNetCore (issue #53).
global using IoPath = System.IO.Path;

using Xunit;

// Disable all parallelism — tests share a single SQL Server container
[assembly: CollectionBehavior(DisableTestParallelization = true)]
