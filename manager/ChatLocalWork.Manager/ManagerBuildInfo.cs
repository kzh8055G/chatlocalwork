using System.Reflection;

namespace ChatLocalWork.Manager;

internal static class ManagerBuildInfo
{
    public static string Version => typeof(ManagerBuildInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? "unknown";
}
