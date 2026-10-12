using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Ada.Native;

[GeneratedComInterface, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr device);
}

[GeneratedComInterface, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    [PreserveSig] int Activate(in Guid iid, int clsCtx, IntPtr activationParams, out IntPtr iface);
}

[GeneratedComInterface, Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal partial interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float db, in Guid ctx);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, in Guid ctx);
    [PreserveSig] int GetMasterVolumeLevel(out float db);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, in Guid ctx);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, in Guid ctx);
    [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
    [PreserveSig] int SetMute(int mute, in Guid ctx);
    [PreserveSig] int GetMute(out int mute);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSize { public int cx, cy; }

[GeneratedComInterface, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
internal partial interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr hbitmap);
}

internal static class ComUtil
{
    public static readonly StrategyBasedComWrappers Wrappers = new();

    public static T? Wrap<T>(IntPtr p) where T : class
    {
        if (p == IntPtr.Zero) return null;
        try { return (T)Wrappers.GetOrCreateObjectForComInstance(p, CreateObjectFlags.None); }
        finally { Marshal.Release(p); }
    }
}
