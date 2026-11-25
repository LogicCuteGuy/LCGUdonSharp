using System;

namespace UdonSharp
{
    /// <summary>
    /// Build-time intrinsics for awaiting VRChat operations. Calls are replaced by the UdonSharp lowerer;
    /// the CLR bodies intentionally cannot schedule work.
    /// </summary>
    public static class VRCAsync
    {
        public static System.Threading.Tasks.Task<VRC.SDK3.StringLoading.IVRCStringDownload> LoadStringAsync(
            VRC.SDKBase.VRCUrl url) => Intrinsic<VRC.SDK3.StringLoading.IVRCStringDownload>();

        public static System.Threading.Tasks.Task LoadStringAsync(VRC.SDKBase.VRCUrl url,
            out VRC.SDK3.StringLoading.IVRCStringDownload result)
        {
            result = null;
            return Intrinsic<VRC.SDK3.StringLoading.IVRCStringDownload>();
        }

        public static System.Threading.Tasks.Task<VRC.SDK3.Image.IVRCImageDownload> LoadImageAsync(
            VRC.SDK3.Image.VRCImageDownloader downloader, VRC.SDKBase.VRCUrl url,
            UnityEngine.Material material = null, VRC.SDK3.Image.TextureInfo textureInfo = null) =>
            Intrinsic<VRC.SDK3.Image.IVRCImageDownload>();

        public static System.Threading.Tasks.Task<VRCVideoLoadResult> LoadVideoAsync(
            VRC.SDK3.Video.Components.Base.BaseVRCVideoPlayer player, VRC.SDKBase.VRCUrl url,
            bool playWhenReady = false) => Intrinsic<VRCVideoLoadResult>();

        public static System.Threading.Tasks.Task<VRCVideoPlaybackResult> WaitForVideoEndAsync(
            VRC.SDK3.Video.Components.Base.BaseVRCVideoPlayer player) => Intrinsic<VRCVideoPlaybackResult>();

        public static System.Threading.Tasks.Task<VRC.SDK3.Rendering.VRCAsyncGPUReadbackRequest>
            RequestGPUReadbackAsync(UnityEngine.Texture source, int mipIndex = 0) =>
            Intrinsic<VRC.SDK3.Rendering.VRCAsyncGPUReadbackRequest>();

        public static System.Threading.Tasks.Task<VRC.Udon.Common.SerializationResult> RequestSerializationAsync() =>
            Intrinsic<VRC.Udon.Common.SerializationResult>();

        public static System.Threading.Tasks.Task<VRC.Economy.IProduct[]> ListAvailableProductsAsync() =>
            Intrinsic<VRC.Economy.IProduct[]>();

        public static System.Threading.Tasks.Task<VRCPlayerPurchasesResult> ListPurchasesAsync(
            VRC.SDKBase.VRCPlayerApi player) => Intrinsic<VRCPlayerPurchasesResult>();

        public static System.Threading.Tasks.Task<VRCProductOwnersResult> ListProductOwnersAsync(
            VRC.Economy.UdonProduct product) => Intrinsic<VRCProductOwnersResult>();

        private static System.Threading.Tasks.Task<T> Intrinsic<T>()
        {
            throw new InvalidOperationException(
                "VRCAsync methods are compiler intrinsics and may only run from compiled UdonSharp code.");
        }
    }

    public sealed class VRCVideoLoadResult
    {
        public VRC.SDK3.Video.Components.Base.BaseVRCVideoPlayer Player { get; internal set; }
        public VRC.SDKBase.VRCUrl Url { get; internal set; }
        public VRC.SDK3.Components.Video.VideoError Error { get; internal set; }
        public bool HasError { get; internal set; }
    }

    public sealed class VRCVideoPlaybackResult
    {
        public VRC.SDK3.Video.Components.Base.BaseVRCVideoPlayer Player { get; internal set; }
        public VRC.SDK3.Components.Video.VideoError Error { get; internal set; }
        public bool HasError { get; internal set; }
    }

    public sealed class VRCPlayerPurchasesResult
    {
        public VRC.SDKBase.VRCPlayerApi Player { get; internal set; }
        public VRC.Economy.IProduct[] Products { get; internal set; }
    }

    public sealed class VRCProductOwnersResult
    {
        public VRC.Economy.UdonProduct Product { get; internal set; }
        public string[] Owners { get; internal set; }
    }

    /// <summary>
    /// Identifies a failure represented by compiler-managed synchronous exception control flow.
    /// </summary>
    public enum UdonExceptionKind
    {
        Unknown,
        Explicit,
        NullReference,
        IndexOutOfRange,
        DivideByZero,
        InvalidOperation,
        Argument,
        ArgumentNull,
        ArgumentOutOfRange,
        NotSupported,
        KeyNotFound,
        Json,
        InvalidCast,
    }

    /// <summary>
    /// Compiler-recognized exception payload. Compiled Udon stores this as scalar hidden state rather
    /// than constructing a managed exception object at runtime.
    /// </summary>
    public sealed class UdonException : Exception
    {
        public UdonExceptionKind Kind { get; }
        public int Code { get; }
        public string Operation { get; }

        public UdonException(UdonExceptionKind kind, string message, int code = 0, string operation = null)
            : base(message)
        {
            Kind = kind;
            Code = code;
            Operation = operation;
        }
    }
}
