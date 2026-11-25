using System;

// Intentionally shares the BCL namespace so UdonSharp source can use familiar syntax.
namespace System.Text.Json
{
    /// <summary>
    /// Options understood by the UdonSharp JSON compiler intrinsic.
    /// </summary>
    public sealed class JsonSerializerOptions
    {
        public bool WriteIndented { get; set; }
    }

    /// <summary>
    /// Represents a JSON encoding or decoding failure in compiled UdonSharp code.
    /// </summary>
    public class JsonException : Exception
    {
        public JsonException() { }
        public JsonException(string message) : base(message) { }
        public JsonException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Compiler intrinsic facade. Calls are replaced with VRCJson-backed code when compiling an
    /// UdonSharpBehaviour and deliberately fail when invoked as ordinary CLR code.
    /// </summary>
    public static class JsonSerializer
    {
        private const string IntrinsicOnlyMessage =
            "System.Text.Json compatibility methods are UdonSharp compiler intrinsics and cannot run as CLR code.";

        public static string Serialize<T>(T value)
        {
            throw new NotSupportedException(IntrinsicOnlyMessage);
        }

        public static string Serialize<T>(T value, JsonSerializerOptions options)
        {
            throw new NotSupportedException(IntrinsicOnlyMessage);
        }

        public static T Deserialize<T>(string json)
        {
            throw new NotSupportedException(IntrinsicOnlyMessage);
        }

        public static bool TrySerialize<T>(T value, out string json, out string error)
        {
            throw new NotSupportedException(IntrinsicOnlyMessage);
        }

        public static bool TryDeserialize<T>(string json, out T value, out string error)
        {
            throw new NotSupportedException(IntrinsicOnlyMessage);
        }
    }
}
