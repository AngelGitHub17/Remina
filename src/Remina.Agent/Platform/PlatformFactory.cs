namespace Remina.Agent.Platform;

public static class PlatformFactory
{
    public static IPlatform Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsPlatform();
        if (OperatingSystem.IsMacOS()) return new MacPlatform();
        if (OperatingSystem.IsLinux()) return new LinuxPlatform();
        throw new PlatformNotSupportedException("OS non pris en charge par l'agent Remina.");
    }
}
