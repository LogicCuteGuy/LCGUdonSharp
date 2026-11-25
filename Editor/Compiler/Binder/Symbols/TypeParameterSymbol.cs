
using System;
using Microsoft.CodeAnalysis;
using UdonSharp.LCGCompiler.Binder;

namespace UdonSharp.LCGCompiler.Symbols
{
    internal class TypeParameterSymbol : TypeSymbol
    {
        public TypeParameterSymbol(ITypeParameterSymbol sourceSymbol, AbstractPhaseContext context) 
            :base(sourceSymbol, context)
        {
        }

        public TypeParameterSymbol(IArrayTypeSymbol arraySymbol, AbstractPhaseContext context)
            :base(arraySymbol, context)
        {
        }

        public override void Bind(BindContext context)
        {
        }

        protected override Symbol CreateSymbol(ISymbol roslynSymbol, AbstractPhaseContext context)
        {
            throw new NotImplementedException();
        }
    }
}
