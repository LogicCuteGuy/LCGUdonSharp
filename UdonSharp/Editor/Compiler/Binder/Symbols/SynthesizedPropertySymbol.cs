
namespace UdonSharp.Compiler.Symbols
{
    internal sealed class SynthesizedPropertySymbol : PropertySymbol
    {
        // Synthesized properties have no Roslyn symbol. Use an accessor's name
        // for diagnostics such as runtime null-guard labels.
        public override string Name => GetMethod?.Name ?? SetMethod?.Name ?? "<synthesized>";

        public SynthesizedPropertySymbol(AbstractPhaseContext context, MethodSymbol getMethod, MethodSymbol setMethod) 
            :base(null, context)
        {
            GetMethod = getMethod;
            SetMethod = setMethod;
        }
    }
}
