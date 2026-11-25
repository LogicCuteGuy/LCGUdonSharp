
using Microsoft.CodeAnalysis;

namespace UdonSharp.LCGCompiler.Symbols
{
    internal class ImportedUdonSharpFieldSymbol : FieldSymbol
    {
        public ImportedUdonSharpFieldSymbol(IFieldSymbol sourceSymbol, AbstractPhaseContext context)
            : base(sourceSymbol, context)
        {

        }
    }
}
