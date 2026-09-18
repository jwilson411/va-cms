namespace VA.CMS.Tests;

/// <summary>
/// DataProtection:KeysPath for test hosts that run outside Development (#168 makes the
/// setting mandatory there). One per-process temp directory; the key ring it holds is
/// throwaway.
/// </summary>
internal static class TestKeyRing
{
    public static readonly string Path =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "va-cms-tests", "dp-keys-" + Environment.ProcessId);
}
