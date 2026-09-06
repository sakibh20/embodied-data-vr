/// <summary>
/// Live, never-cached pointer to the active run's settings, published by
/// SessionBootstrap.Awake(). Mirrors the PlayerRig pattern: consumers (SessionUI)
/// read this instead of each holding/finding their own RunSettings reference.
/// Null-safe to read before a bootstrap has run (falls back to defaults).
/// </summary>
public static class StudyConfig
{
    public static RunSettings Settings;
}
