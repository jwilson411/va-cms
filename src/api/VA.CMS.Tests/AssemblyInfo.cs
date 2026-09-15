using Xunit;

// Disable all parallelism — tests share a single SQL Server container
[assembly: CollectionBehavior(DisableTestParallelization = true)]
