using System.Reflection;

namespace ImageMason;

internal static class AppInfo
{
    /// <summary>The build's version (set with -p:Version), such as 0.1.0.</summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public static string UserAgent { get; } = $"image-mason/{Version}";
}
