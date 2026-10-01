namespace AIUsage.Core;

/// Separate local-only releases from legacy instances which may still have account access.
public static class InstancePolicy
{
    public static string InstanceName(bool demo) => demo ? @"Local\AIUsage.LocalOnly.v1.Demo.Instance" : @"Local\AIUsage.LocalOnly.v1.Instance";
    public static string EventName(bool demo) => demo ? @"Local\AIUsage.LocalOnly.v1.Demo.Show" : @"Local\AIUsage.LocalOnly.v1.Show";
    public static string LegacyName(bool demo) => demo ? @"Local\AIUsage.Windows.Demo.Instance" : @"Local\AIUsage.Windows.Instance";
    public static bool LegacyRunning(bool demo)
    {
        try
        {
            if (!Mutex.TryOpenExisting(LegacyName(demo), out var existing)) return false;
            existing.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException) { return true; } // Do not activate an unverified instance.
    }
}
