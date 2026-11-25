
using UdonSharp.LCGCompiler.Symbols;

namespace UdonSharp.LCGCompiler.Assembly
{
    internal class JumpLabel
    {
        public MethodSymbol DebugMethod { get; set; }
        
        public uint Address { get; set; } = uint.MaxValue;
    }
}
