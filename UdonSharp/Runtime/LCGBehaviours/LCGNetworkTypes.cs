using JetBrains.Annotations;

namespace UdonSharp
{
    [PublicAPI]
    public enum LCGZoneExitMode
    {
        Freeze,
        RestoreDefaults,
        DisableChildren,
    }

    /// <summary>Wire type identifiers used by compiler-generated packet frames.</summary>
    public enum LCGPacketType : byte
    {
        Boolean = 1,
        SByte = 2,
        Byte = 3,
        Int16 = 4,
        UInt16 = 5,
        Int32 = 6,
        UInt32 = 7,
        Int64 = 8,
        UInt64 = 9,
        Single = 10,
        Double = 11,
        Char = 12,
        String = 13,
        Vector2 = 14,
        Vector3 = 15,
        Vector4 = 16,
        Quaternion = 17,
        Color = 18,
        Color32 = 19,
        ArrayFlag = 128,
    }

}
