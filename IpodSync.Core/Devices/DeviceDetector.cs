using System.Runtime.InteropServices;

namespace IpodSync.Core.Devices;

public record IpodDevice(string MountPath, string Label, long FreeBytes, long TotalBytes)
{
    public string MusicPath(string folder) => Path.Combine(MountPath, folder);
    public double FreeGb  => FreeBytes  / 1024d / 1024d / 1024d;
    public double TotalGb => TotalBytes / 1024d / 1024d / 1024d;
}

public interface IDeviceDetector
{
    /// <summary>
    /// Busca un iPod conectado. Lo identifica por la carpeta .rockbox
    /// en la raíz, NO por letra de unidad ni etiqueta: así funciona
    /// aunque el sistema le asigne otro punto de montaje, y evita que
    /// un borrado apunte al disco equivocado.
    /// </summary>
    IpodDevice? Detect();
}

public static class DeviceDetectorFactory
{
    public static IDeviceDetector Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsDeviceDetector();
        return new LinuxDeviceDetector();
    }
}

/// <summary>Marcador que identifica un iPod con Rockbox.</summary>
internal static class Marker
{
    public const string RockboxFolder = ".rockbox";

    public static bool LooksLikeIpod(string rootPath)
    {
        try { return Directory.Exists(Path.Combine(rootPath, RockboxFolder)); }
        catch { return false; }
    }
}

public class WindowsDeviceDetector : IDeviceDetector
{
    public IpodDevice? Detect()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                // El iPod puede aparecer como Removable o como Fixed
                // según el adaptador; no filtramos por tipo.
                if (!Marker.LooksLikeIpod(drive.RootDirectory.FullName)) continue;

                return new IpodDevice(
                    drive.RootDirectory.FullName,
                    string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "IPOD" : drive.VolumeLabel,
                    drive.AvailableFreeSpace,
                    drive.TotalSize);
            }
            catch
            {
                // Unidad que Windows lista pero no puede abrir
                // (pasa con la partición de firmware del iPod).
            }
        }
        return null;
    }
}

public class LinuxDeviceDetector : IDeviceDetector
{
    private static readonly string[] MountRoots =
    {
        "/media", "/run/media", "/mnt"
    };

    public IpodDevice? Detect()
    {
        foreach (var candidate in EnumerateCandidates())
        {
            if (!Marker.LooksLikeIpod(candidate)) continue;
            try
            {
                var di = new DriveInfo(candidate);
                return new IpodDevice(
                    candidate,
                    Path.GetFileName(candidate.TrimEnd('/')),
                    di.AvailableFreeSpace,
                    di.TotalSize);
            }
            catch
            {
                return new IpodDevice(candidate, Path.GetFileName(candidate), 0, 0);
            }
        }
        return null;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        foreach (var root in MountRoots)
        {
            if (!Directory.Exists(root)) continue;

            // /media/IPOD  o  /media/usuario/IPOD
            IEnumerable<string> level1;
            try { level1 = Directory.EnumerateDirectories(root); }
            catch { continue; }

            foreach (var d in level1)
            {
                yield return d;
                IEnumerable<string> level2;
                try { level2 = Directory.EnumerateDirectories(d); }
                catch { continue; }
                foreach (var d2 in level2) yield return d2;
            }
        }
    }
}
