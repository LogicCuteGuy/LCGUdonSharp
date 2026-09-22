
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using System.Linq;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Binder;
using UdonSharp.Compiler.Emit;
using UdonSharp.Compiler.Udon;
using UdonSharp.Core;
using UdonSharp.Localization;


namespace UdonSharp.Compiler.Symbols
{
    internal class UdonSharpBehaviourMethodSymbol : MethodSymbol
    {
        public ExportAddress ExportedMethodAddress { get; }
        
        public bool NeedsExportFromReference { get; private set; }

        /// <summary>
        /// Marks the symbol as one that's referenced from some behaviour non-locally
        /// This allows private/protected/internal methods to be called from behaviours that have the access permissions
        /// </summary>
        public void MarkNeedsReferenceExport() => NeedsExportFromReference = true;
        
        public UdonSharpBehaviourMethodSymbol(IMethodSymbol sourceSymbol, AbstractPhaseContext context)
            : base(sourceSymbol, context)
        {
            ExportedMethodAddress = new ExportAddress(ExportAddress.AddressKind.String, this);
        }

        private static readonly HashSet<string> _obsoleteOverrides = new HashSet<string>()
        {
            "OnStationEntered",
            "OnStationExited",
            "OnOwnershipTransferred",
        };

        public override void Bind(BindContext context)
        {
            IMethodSymbol symbol = RoslynSymbol;

            if (symbol.MethodKind == MethodKind.Constructor && !symbol.IsImplicitlyDeclared)
                throw new NotSupportedException(LocStr.CE_UdonSharpBehaviourConstructorsNotSupported, symbol.Locations.FirstOrDefault());
            if (symbol.IsGenericMethod)
                throw new NotSupportedException(LocStr.CE_UdonSharpBehaviourGenericMethodsNotSupported, symbol.Locations.FirstOrDefault());

            if (symbol.Parameters.Length == 0 && _obsoleteOverrides.Contains(symbol.Name))
                throw new NotSupportedException($"The {symbol.Name}() event is deprecated use the version with the VRCPlayerApi '{symbol.Name}(VRCPlayerApi player)' instead");

            base.Bind(context);
            
            if (IsUntypedGenericMethod)
                throw new NotSupportedException(LocStr.CE_UdonSharpBehaviourGenericMethodsNotSupported, symbol.Locations.FirstOrDefault());
        }

        public override void Emit(EmitContext context)
        {
            EmitContext.MethodLinkage methodLinkage = context.GetMethodLinkage(this, false);

            if (RoslynSymbol.MethodKind == MethodKind.PropertySet)
            {
                UdonSharpBehaviourPropertySymbol owningProperty = context.GetSymbol(RoslynSymbol.AssociatedSymbol) as UdonSharpBehaviourPropertySymbol;

                if (owningProperty != null && 
                    owningProperty.CallbackSymbol != null)
                {
                    context.Module.AddFieldChangeExportTag(owningProperty.CallbackSymbol);

                    Value fieldValue = context.GetUserValue(owningProperty.CallbackSymbol);
                    Value oldValueVal = context.TopTable.CreateParameterValue($"_old_{fieldValue.UniqueID}", fieldValue.UserType);
                    Value methodValueParam = context.GetMethodLinkage(this, false).ParameterValues[0];

                    context.Module.AddCopy(fieldValue, methodValueParam);
                    context.Module.AddCopy(oldValueVal, fieldValue);
                }
            }
            
            bool needsExport = context.MethodNeedsExport(this);
            if (needsExport)
            {
                ExportedMethodAddress.ResolveAddress(methodLinkage.MethodExportName);
                context.Module.AddExportTag(this);
            }

            Value returnAddressConst = context.GetConstantValue(context.GetTypeSymbol(SpecialType.System_UInt32), 0xFFFFFFFF);
            if (needsExport && context.HasExceptionSupport)
            {
                context.ClearExceptionState();
                JumpLabel rootExit = context.Module.CreateLabel();
                Value rootExitAddress = context.CreateGlobalInternalValue(context.GetTypeSymbol(SpecialType.System_UInt32));
                context.Module.AddPush(rootExitAddress);
                context.Module.AddJump(methodLinkage.MethodLabel);
                context.Module.LabelJump(rootExit);
                rootExitAddress.DefaultValue = rootExit.Address;
                context.EmitUnhandledExceptionAndReturn(returnAddressConst);
            }
            else
            {
                context.Module.AddPush(returnAddressConst);
            }

            base.Emit(context);
        }
    }
}
