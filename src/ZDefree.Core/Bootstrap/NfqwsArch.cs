using System.Runtime.InteropServices;

namespace ZDefree.Core.Bootstrap;

public enum NfqwsArch
{
    Auto,
    LinuxX64,
    LinuxX86,
    LinuxArm,
    LinuxArm64,
}

public static class NfqwsArchHelper
{
    /// <summary>
    /// Tarball directory name for the given arch — matches bol-van/zapret's
    /// release tarball layout: <c>binaries/&lt;subdir&gt;/nfqws</c>.
    /// </summary>
    public static string ResolveSubdir(NfqwsArch arch) =>
        (arch == NfqwsArch.Auto ? DetectArch() : arch) switch
        {
            NfqwsArch.LinuxX64   => "linux-x86_64",
            NfqwsArch.LinuxX86   => "linux-x86",
            NfqwsArch.LinuxArm   => "linux-arm",
            NfqwsArch.LinuxArm64 => "linux-arm64",
            _                    => "linux-x86_64",
        };

    /// <summary>
    /// Detect the current host's Linux arch. On Windows/macOS we still return
    /// a sensible default (LinuxX64) so cross-build/dry-run from those platforms
    /// works for testing.
    /// </summary>
    public static NfqwsArch DetectArch()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64   => NfqwsArch.LinuxX64,
            Architecture.X86   => NfqwsArch.LinuxX86,
            Architecture.Arm   => NfqwsArch.LinuxArm,
            Architecture.Arm64 => NfqwsArch.LinuxArm64,
            _                  => NfqwsArch.LinuxX64,
        };
    }
}
