// Test fixture, never shipped: an innocuous marker proves whether a startup hook ran.
public class StartupHook
{
    public static void Initialize()
    {
        string? marker = Environment.GetEnvironmentVariable("AIUSAGE_TEST_HOOK_MARKER");
        if (marker is not null) File.WriteAllText(marker, "synthetic startup hook executed");
    }
}
