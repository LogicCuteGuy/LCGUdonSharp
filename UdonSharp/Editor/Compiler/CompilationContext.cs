
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using UdonSharp.Compiler.Assembly;
using UdonSharp.Compiler.Binder;
using UdonSharp.Compiler.Symbols;
using UdonSharp.Compiler.Udon;
using UdonSharp.Core;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;

[assembly: InternalsVisibleTo("LogicCuteGuy.LCGUdonSharp.Installer.Editor.Tests")]

namespace UdonSharp.Compiler
{
    using SyntaxTree = Microsoft.CodeAnalysis.SyntaxTree;

    internal enum DiagnosticSeverity
    {
        Log,
        Warning,
        Error,
    }
    
    internal class ModuleBinding
    {
        public SyntaxTree tree;
        public string filePath;
        public string sourceText;
        public SemanticModel semanticModel; // Populated after Roslyn compile
        public AssemblyModule assemblyModule;
        public UdonSharpProgramAsset programAsset;
        public Type programClass;
        public MonoScript programScript;
        public BindContext binding;
        public string assembly;
        public Lowering.ExtendedLoweringPlan loweringPlan;
        public bool asyncSyntaxLowered;
    }
    
    internal class CompilationContext
    {
        internal const string LCGNetworkDiagnosticsDefine = "LCG_NETWORK_DIAGNOSTICS";

        internal class CompileDiagnostic
        {
            public DiagnosticSeverity Severity { get; }
            public Location Location { get; }
            public string Message { get; }

            public CompileDiagnostic(DiagnosticSeverity severity, Location location, string message)
            {
                Severity = severity;
                Location = location;
                Message = message;
            }
        }
        
        /// <summary>
        /// High level phase of the compiler
        /// </summary>
        public enum CompilePhase
        {
            Setup,
            RoslynCompile,
            /// <summary>
            /// Roslyn has run its compilation and error checking, we are now binding all symbol references and solving for dependencies.
            /// </summary>
            Bind,
            /// <summary>
            /// Rewrites extended C# constructs into Udon-safe bound nodes and build-time specializations.
            /// </summary>
            Lower,
            /// <summary>
            /// Emitting the assembly modules' uasm instructions and serialized heap values for each program
            /// </summary>
            Emit,
            Count,
        }
        
        public CompilePhase CurrentPhase { get; set; }
        
        public float PhaseProgress { get; set; }

        private int _errorCount;

        public int ErrorCount => _errorCount;
        
        public CSharpCompilation RoslynCompilation { get; set; }

        public ConcurrentBag<CompileDiagnostic> Diagnostics { get; } = new ConcurrentBag<CompileDiagnostic>();
        
        public ModuleBinding[] ModuleBindings { get; private set; }
        
        private ConcurrentDictionary<ITypeSymbol, TypeSymbol> _typeSymbolLookup = new ConcurrentDictionary<ITypeSymbol, TypeSymbol>();

        public UdonSharpCompileOptions Options { get; }
        
        private Dictionary<TypeSymbol, ImmutableArray<TypeSymbol>> _inheritedTypes;

        public CompilationContext(UdonSharpCompileOptions options)
        {
            Options = options;
        }

        public TypeSymbol GetTypeSymbol(ITypeSymbol type, AbstractPhaseContext context)
        {
            TypeSymbol typeSymbol = _typeSymbolLookup.GetOrAdd(type, (key) => TypeSymbolFactory.CreateSymbol(type, context));

            return typeSymbol;
        }

        public TypeSymbol GetUdonTypeSymbol(ITypeSymbol type, AbstractPhaseContext context)
        {
            if (!TypeSymbol.TryGetSystemType(type, out var systemType))
                throw new InvalidOperationException("foundType should not be null");
            
            systemType = UdonSharpUtils.UserTypeToUdonType(systemType);
            
            return GetTypeSymbol(systemType, context);
        }

        public TypeSymbol GetTypeSymbol(Type systemType, AbstractPhaseContext context)
        {
            int arrayDepth = 0;
            while (systemType.IsArray)
            {
                arrayDepth++;
                systemType = systemType.GetElementType();
            }
            
            ITypeSymbol typeSymbol = RoslynCompilation.GetTypeByMetadataName(systemType.FullName);

            for (int i = 0; i < arrayDepth; ++i)
                typeSymbol = RoslynCompilation.CreateArrayTypeSymbol(typeSymbol, 1);
            
            return GetTypeSymbol(typeSymbol, context);
        }

        public TypeSymbol GetTypeSymbol(SpecialType type, AbstractPhaseContext context)
        {
            return GetTypeSymbol(RoslynCompilation.GetSpecialType(type), context);
        }

        public Symbol GetSymbol(ISymbol sourceSymbol, AbstractPhaseContext context)
        {
            if (sourceSymbol == null)
                throw new NullReferenceException("Source symbol cannot be null");
            
            if (sourceSymbol is ITypeSymbol typeSymbol)
                return GetTypeSymbol(typeSymbol, context);

            if (sourceSymbol.ContainingType != null)
                return GetTypeSymbol(sourceSymbol.ContainingType, context).GetMember(sourceSymbol, context);

            throw new InvalidOperationException($"Could not get symbol for {sourceSymbol}");
        }

        public SemanticModel GetSemanticModel(SyntaxTree modelTree)
        {
            return RoslynCompilation.GetSemanticModel(modelTree);
        }

        public void AddDiagnostic(DiagnosticSeverity severity, SyntaxNode node, string message)
        {
            Diagnostics.Add(new CompileDiagnostic(severity, node?.GetLocation(), message));

            if (severity == DiagnosticSeverity.Error)
                Interlocked.Increment(ref _errorCount);
        }
        
        public void AddDiagnostic(DiagnosticSeverity severity, Location location, string message)
        {
            Diagnostics.Add(new CompileDiagnostic(severity, location, message));
            
            if (severity == DiagnosticSeverity.Error)
                Interlocked.Increment(ref _errorCount);
        }

        public ModuleBinding[] LoadSyntaxTreesAndCreateModules(IEnumerable<CompilationContext.ScriptAssembly> assemblies)
        {
            ConcurrentBag<ModuleBinding> syntaxTrees = new ConcurrentBag<ModuleBinding>();

            foreach (ScriptAssembly scriptAssembly in assemblies)
            {
                Parallel.ForEach(scriptAssembly.SourceFiles, (currentSource) =>
                {
                    string programSource = UdonSharpUtils.ReadFileTextSync(currentSource);

#if UNITY_2022_3_OR_NEWER
                    const LanguageVersion version = LanguageVersion.CSharp9;
#else
                    const LanguageVersion version = LanguageVersion.CSharp7_3;
#endif
                    
                    SyntaxTree programSyntaxTree = CSharpSyntaxTree.ParseText(programSource, CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.None).WithPreprocessorSymbols(scriptAssembly.Defines).WithLanguageVersion(version));

                    syntaxTrees.Add(new ModuleBinding() { tree = programSyntaxTree, filePath = currentSource, sourceText = programSource });
                });
            }
            
            ModuleBindings = syntaxTrees.ToArray();
            
            return ModuleBindings;
        }
        
        internal class ScriptAssembly
        {
            public List<string> SourceFiles { get; } = new();
            public List<string> Defines { get; } = new();
        }
        
        private static Dictionary<string, IEnumerable<ScriptAssembly>> _builtScriptCache = new Dictionary<string, IEnumerable<ScriptAssembly>>();

        private static string GetBuildAssemblyCacheKey(bool isEditorBuild, BuildTarget buildTarget, bool lcgNetworkDiagnostics)
        {
            return $"{buildTarget}_{(isEditorBuild ? "editor" : "runtime")}_{lcgNetworkDiagnostics}";
        }

        public static IEnumerable<ScriptAssembly> GetBuildAssemblies(bool isEditorBuild, BuildTarget buildTarget)
        {
            bool lcgNetworkDiagnostics = UdonSharpSettings.GetSettings().lcgNetworkDiagnostics;
            string cacheKey = GetBuildAssemblyCacheKey(isEditorBuild, buildTarget, lcgNetworkDiagnostics);
            if (_builtScriptCache.TryGetValue(cacheKey, out var cachedPaths))
                return cachedPaths;
            
            List<ScriptAssembly> scriptAssemblies = new List<ScriptAssembly>();

            foreach (UnityEditor.Compilation.Assembly asm in CompilationPipeline.GetAssemblies(isEditorBuild ? AssembliesType.Editor : AssembliesType.PlayerWithoutTestAssemblies))
            {
                if (asm.name == "Assembly-CSharp" || IsUdonSharpAssembly(asm.name))
                {
                    string[] assemblySourcePaths = UdonSharpSettings.FilterBlacklistedPaths(asm.sourceFiles).ToArray();
                    
                    if (assemblySourcePaths.Length == 0)
                        continue;
                    
                    ScriptAssembly scriptAssembly = new();

                    scriptAssembly.SourceFiles.AddRange(assemblySourcePaths);
                    scriptAssembly.Defines.AddRange(UdonSharpUtils.GetProjectDefines(asm.defines, isEditorBuild, buildTarget));
                    if (lcgNetworkDiagnostics)
                        scriptAssembly.Defines.Add(LCGNetworkDiagnosticsDefine);
                    
                    scriptAssemblies.Add(scriptAssembly);
                }
            }
            
            _builtScriptCache.Add(cacheKey, scriptAssemblies);

            return scriptAssemblies;
        }
        
        private static Dictionary<bool, IEnumerable<string>> _scriptPathCache = new Dictionary<bool, IEnumerable<string>>();
        

        public static IEnumerable<string> GetAllFilteredSourcePaths(bool isEditorBuild)
        {
            if (_scriptPathCache.TryGetValue(isEditorBuild, out var cachedPaths))
                return cachedPaths;
            
            HashSet<string> assemblySourcePaths = new HashSet<string>();

            foreach (UnityEditor.Compilation.Assembly asm in CompilationPipeline.GetAssemblies(isEditorBuild ? AssembliesType.Editor : AssembliesType.PlayerWithoutTestAssemblies))
            {
                if (asm.name == "Assembly-CSharp" || IsUdonSharpAssembly(asm.name))
                    assemblySourcePaths.UnionWith(asm.sourceFiles);
            }

            IEnumerable<string> paths =  UdonSharpSettings.FilterBlacklistedPaths(assemblySourcePaths);
            
            _scriptPathCache.Add(isEditorBuild, paths);

            return paths;
        }
        
        internal static void ResetAssemblyCaches()
        {
            _scriptPathCache.Clear();
            _builtScriptCache.Clear();
            _udonSharpAssemblyNames = null;
            CompilerUdonInterface.ResetAssemblyCache(); 
        }
        
        public static IEnumerable<MonoScript> GetAllFilteredScripts(bool isEditorBuild)
        {
            return GetAllFilteredSourcePaths(isEditorBuild).Select(AssetDatabase.LoadAssetAtPath<MonoScript>).Where(e => e != null).ToArray();
        }

        private static HashSet<string> _udonSharpAssemblyNames;

        private static bool IsUdonSharpAssembly(string assemblyName)
        {
            if (_udonSharpAssemblyNames == null)
            {
                _udonSharpAssemblyNames = new HashSet<string>();
                foreach (UdonSharpAssemblyDefinition asmDef in CompilerUdonInterface.UdonSharpAssemblyDefinitions)
                {
                    _udonSharpAssemblyNames.Add(asmDef.sourceAssembly.name);
                }
            }

            return _udonSharpAssemblyNames.Contains(assemblyName);
        }

        private static List<MetadataReference> _metadataReferences;

        public static IEnumerable<MetadataReference> GetMetadataReferences()
        {
            if (_metadataReferences != null) return _metadataReferences;
            
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            _metadataReferences = new List<MetadataReference>();

            foreach (var assembly in assemblies)
            {
                if (assembly.IsDynamic || assembly.Location.Length <= 0 ||
                    assembly.Location.StartsWith("data")) 
                    continue;
                
                if (assembly.GetName().Name == "Assembly-CSharp" ||
                    assembly.GetName().Name == "Assembly-CSharp-Editor")
                {
                    continue;
                }

                if (IsUdonSharpAssembly(assembly.GetName().Name))
                    continue;

                PortableExecutableReference executableReference = null;

                try
                {
                    executableReference = MetadataReference.CreateFromFile(assembly.Location);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Unable to locate assembly {assembly.Location} Exception: {e}");
                }

                if (executableReference != null)
                    _metadataReferences.Add(executableReference);
            }

            return _metadataReferences;
        }
        
        public string TranslateLocationToFileName(Location location)
        {
            if (location == null) return null;
            
            SyntaxTree locationSyntaxTree = location.SourceTree;

            if (locationSyntaxTree == null) return null;

            ModuleBinding binding = ModuleBindings.FirstOrDefault(e => e.tree == locationSyntaxTree);

            if (binding == null) return null;

            return binding.filePath;
        }

        public class MethodExportLayout
        {
            public MethodSymbol Method { get; }
            
            public string ExportMethodName { get; }
            
            public string ReturnExportName { get; }
            
            public string[] ParameterExportNames { get; }

            public MethodExportLayout(MethodSymbol method, string exportMethodName, string returnExportName, string[] parameterExportNames)
            {
                Method = method;
                ExportMethodName = exportMethodName;
                ReturnExportName = returnExportName;
                ParameterExportNames = parameterExportNames;
            }
        }

        private class TypeLayout
        {
            private ImmutableDictionary<MethodSymbol, MethodExportLayout> MethodLayouts { get; }
            public ImmutableDictionary<string, int> SymbolCounters { get; }

            public TypeLayout(Dictionary<MethodSymbol, MethodExportLayout> methodLayouts, Dictionary<string, int> symbolCounters)
            {
                MethodLayouts = methodLayouts.ToImmutableDictionary();
                SymbolCounters = symbolCounters.ToImmutableDictionary();
            }
        }

        private object _layoutLock = new object();
        
        private Dictionary<MethodSymbol, MethodExportLayout> _layouts =
            new Dictionary<MethodSymbol, MethodExportLayout>();

        private Dictionary<TypeSymbol, TypeLayout> _builtLayouts = new Dictionary<TypeSymbol, TypeLayout>();

        private TypeSymbol _udonSharpBehaviourType;

        static string GetUniqueID(Dictionary<string, int> idLookup, string id)
        {
            if (!idLookup.TryGetValue(id, out var foundID))
            {
                idLookup.Add(id, 0);
            }

            idLookup[id] += 1;

            return $"__{foundID}_{id}";
        }

        private MethodExportLayout BuildMethodLayout(MethodSymbol methodSymbol, Dictionary<string, int> idLookup, Dictionary<string, bool> networkCallableDedup)
        {
            if (TryGetInterfaceContractMethod(methodSymbol, out IMethodSymbol interfaceMethod))
            {
                if (!methodSymbol.IsStatic && !CompilerUdonInterface.IsUdonEvent(methodSymbol) &&
                    CompilerUdonInterface.IsUdonEventName(methodSymbol.Name))
                    throw new CompilerException($"Interface method with built-in event name '{methodSymbol.Name}' is not supported.",
                        methodSymbol.RoslynSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.GetLocation());

                if (methodSymbol.SymbolAttributes != null &&
                    (methodSymbol.HasAttribute<NetworkCallableAttribute>() || methodSymbol.HasAttribute<LCGPacketAttribute>()))
                    throw new CompilerException($"Interface method implementation '{methodSymbol.Name}' cannot be marked [NetworkCallable] or [LCGPacket].",
                        methodSymbol.RoslynSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.GetLocation());

                MethodExportLayout interfaceLayout = BuildInterfaceMethodLayout(methodSymbol, interfaceMethod);
                _layouts.Add(methodSymbol, interfaceLayout);
                return interfaceLayout;
            }

            string methodName = methodSymbol.Name;
            string[] paramNames = new string[methodSymbol.Parameters.Length];
            string returnName = null;

            if (!methodSymbol.IsStatic && !CompilerUdonInterface.IsUdonEvent(methodSymbol) && CompilerUdonInterface.IsUdonEventName(methodName))
            {
                // If the user has declared an event with the same name as a built-in one but with different arguments, we imitate Unity here and complain for our built-in events
                throw new CompilerException($"Method with same name as built-in event '{methodSymbol}' cannot be declared with parameter types that do not match the event.", methodSymbol.RoslynSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.GetLocation());
            }
            
            if (CompilerUdonInterface.IsUdonEvent(methodSymbol))
            {
                ImmutableArray<(string, Type)> paramArgs = CompilerUdonInterface.GetUdonEventArgs(methodName);
                methodName = CompilerUdonInterface.GetUdonEventName(methodName);

                for (int i = 0; i < paramNames.Length && i < paramArgs.Length; ++i)
                    paramNames[i] = paramArgs[i].Item1;

                if (methodSymbol.SymbolAttributes != null &&
                    (methodSymbol.HasAttribute<NetworkCallableAttribute>() || methodSymbol.HasAttribute<LCGPacketAttribute>()))
                {
                    AddDiagnostic(DiagnosticSeverity.Error, methodSymbol.RoslynSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.GetLocation(),
                        $"Built-in Udon event '{methodName}' cannot be marked [NetworkCallable] or [LCGPacket].");
                }
            }
            else if (methodSymbol.SymbolAttributes != null &&
                     (methodSymbol.HasAttribute<NetworkCallableAttribute>() || methodSymbol.HasAttribute<LCGPacketAttribute>()))
            {
                // Do not mangle network callable methods, we guarantee they are unique later on and other scripts may call them by name
                // Their parameters are fair game though, as long as we encode the mangled version into the metadata too
                for (int i = 0; i < paramNames.Length; ++i)
                    paramNames[i] = GetUniqueID(idLookup, methodSymbol.Parameters[i].Name + "__param");

                // Explicitly forbid overloading even with non-network callable methods (idLookup or unmangled)
                if (networkCallableDedup.ContainsKey(methodName) || idLookup.ContainsKey(methodName))
                {
                    AddDiagnostic(DiagnosticSeverity.Error, methodSymbol.RoslynSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.GetLocation(),
                        $"Duplicate network callable method name '{methodName}'. Overloading of any kind is not supported.");
                }

                // Validate function itself
                ValidateNetworkCallableMethod(methodSymbol, methodName);

                LCGPacketAttribute packetAttribute = methodSymbol.GetAttribute<LCGPacketAttribute>();
                if (packetAttribute != null && !string.IsNullOrEmpty(packetAttribute.Callback))
                {
                    AddDiagnostic(DiagnosticSeverity.Error,
                        methodSymbol.RoslynSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.GetLocation(),
                        $"LCG packet method '{methodName}' cannot declare Callback; Callback is only valid on fields.");
                }

                if (packetAttribute != null && methodSymbol.HasAttribute<NetworkCallableAttribute>())
                {
                    AddDiagnostic(DiagnosticSeverity.Error,
                        methodSymbol.RoslynSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.GetLocation(),
                        $"LCG packet method '{methodName}' cannot also be marked [NetworkCallable].");
                }

                networkCallableDedup[methodName] = true; // yes, we are network callable
            }
            else
            {
                // Explicitly forbid overloading with network callable methods, but not if none of the overloads are network callable
                if (networkCallableDedup.TryGetValue(methodName, out var isNetworkCallable) && isNetworkCallable)
                {
                    AddDiagnostic(DiagnosticSeverity.Error, methodSymbol.RoslynSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.GetLocation(),
                        $"Duplicate network callable method name '{methodName}'. Overloading of any kind is not supported.");
                }
                networkCallableDedup[methodName] = false; // we exist, but we are not network callable

                // Regular method handling with optional mangling:

                if (methodSymbol.Parameters.Length > 0) // Do not mangle 0 parameter methods as they may be called externally
                    methodName = GetUniqueID(idLookup, methodName);

                for (int i = 0; i < paramNames.Length; ++i)
                    paramNames[i] = GetUniqueID(idLookup, methodSymbol.Parameters[i].Name + "__param");
            }

            if (methodSymbol.ReturnType != null)
                returnName = GetUniqueID(idLookup, methodName + "__ret");

            MethodExportLayout exportLayout = new MethodExportLayout(methodSymbol, methodName, returnName, paramNames);
            
            _layouts.Add(methodSymbol, exportLayout);

            return exportLayout;
        }

        private static bool TryGetInterfaceContractMethod(MethodSymbol methodSymbol, out IMethodSymbol interfaceMethod)
        {
            IMethodSymbol roslynMethod = methodSymbol.RoslynSymbol;
            INamedTypeSymbol containingType = roslynMethod.ContainingType;

            if (containingType.TypeKind == TypeKind.Interface)
            {
                interfaceMethod = roslynMethod;
                return true;
            }

            foreach (INamedTypeSymbol candidateInterface in containingType.AllInterfaces)
            {
                if (candidateInterface.IsExternType())
                    continue;

                foreach (IMethodSymbol candidateMethod in candidateInterface.GetMembers().OfType<IMethodSymbol>())
                {
                    ISymbol implementation = containingType.FindImplementationForInterfaceMember(candidateMethod);
                    if (SymbolEqualityComparer.Default.Equals(implementation, roslynMethod) ||
                        SymbolEqualityComparer.Default.Equals(implementation?.OriginalDefinition, roslynMethod.OriginalDefinition))
                    {
                        interfaceMethod = candidateMethod;
                        return true;
                    }
                }
            }

            interfaceMethod = null;
            return false;
        }

        private static MethodExportLayout BuildInterfaceMethodLayout(MethodSymbol methodSymbol, IMethodSymbol interfaceMethod)
        {
            string signature = interfaceMethod.Name + "(" +
                               string.Join(",", interfaceMethod.Parameters.Select(parameter =>
                                   parameter.RefKind + ":" + parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) +
                               ")->" + interfaceMethod.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            ulong signatureHash = 14695981039346656037UL;
            foreach (char character in signature)
            {
                signatureHash ^= (byte)character;
                signatureHash *= 1099511628211UL;
                signatureHash ^= (byte)(character >> 8);
                signatureHash *= 1099511628211UL;
            }

            string safeMethodName = new string(interfaceMethod.Name
                .Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_')
                .ToArray());
            string exportName = interfaceMethod.Parameters.Length == 0
                ? safeMethodName
                : $"__intf_{safeMethodName}_{signatureHash:x16}";
            string[] parameterNames = Enumerable.Range(0, interfaceMethod.Parameters.Length)
                .Select(index => $"{exportName}__param_{index}")
                .ToArray();
            string returnName = interfaceMethod.ReturnsVoid ? null : $"{exportName}__ret";

            return new MethodExportLayout(methodSymbol, exportName, returnName, parameterNames);
        }

        /// <summary>
        /// We only allow the simplest form of method declaration for [NetworkCallable] to avoid confusing semantics or future compatiblity issues.
        /// This method checks a given declaration for conformity.
        /// </summary>
        private void ValidateNetworkCallableMethod(MethodSymbol methodSymbol, string methodName)
        {
            void FailValidation(string error)
                => AddDiagnostic(DiagnosticSeverity.Error, methodSymbol.RoslynSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.GetLocation(), error);

            if (methodSymbol.RoslynSymbol.DeclaredAccessibility != Accessibility.Public)
                FailValidation($"Network callable method '{methodName}' must be public.");
            if (methodSymbol.IsGenericMethod)
                FailValidation($"Network callable method '{methodName}' cannot be generic.");
            if (methodSymbol.IsExtern)
                FailValidation($"Network callable method '{methodName}' cannot be extern.");
            if (methodSymbol.IsOperator)
                FailValidation($"Network callable method '{methodName}' cannot be an operator.");
            if (methodSymbol.RoslynSymbol.IsVirtual)
                FailValidation($"Network callable method '{methodName}' cannot be virtual.");
            if (methodSymbol.RoslynSymbol.IsAbstract)
                FailValidation($"Network callable method '{methodName}' cannot be abstract.");
            if (methodSymbol.RoslynSymbol.IsOverride)
                FailValidation($"Network callable method '{methodName}' cannot be an override.");
            if (methodSymbol.RoslynSymbol.IsVararg) // this is not `params` but something else cursed :/
                FailValidation($"Network callable method '{methodName}' cannot be vararg.");
            if (methodSymbol.RoslynSymbol.IsAsync) // not supported anyway
                FailValidation($"Network callable method '{methodName}' cannot be async.");
            if (methodSymbol.RoslynSymbol.IsStatic)
                FailValidation($"Network callable method '{methodName}' cannot be static.");
            if (methodSymbol.RoslynSymbol.IsSealed) // what does this mean?
                FailValidation($"Network callable method '{methodName}' cannot be sealed.");
            if (methodSymbol.RoslynSymbol.ExplicitInterfaceImplementations.Length > 0)
                FailValidation($"Network callable method '{methodName}' cannot be an explicit interface implementation.");
            if (methodSymbol.ReturnType != null)
                FailValidation($"Network callable method '{methodName}' cannot have a return type.");

            // parameter validation
            if (methodSymbol.Parameters.Length > 8)
                FailValidation($"Network callable method '{methodName}' cannot have more than 8 parameters.");
            foreach (var parameter in methodSymbol.Parameters)
            {
                if (parameter.IsByRef)
                    FailValidation($"Network callable method '{methodName}' cannot have `ref` or `out` parameters.");
                if (parameter.IsParams)
                    FailValidation($"Network callable method '{methodName}' cannot use `params`.");
                if (parameter.DefaultValue != null)
                    FailValidation($"Network callable method '{methodName}' cannot have parameters with default values.");
            }
        }

        /// <summary>
        /// Builds a layout for a given type.
        /// First traverses all base types and builds their layouts when needed since the base type layouts inform the layout of derived types.
        /// </summary>
        private void BuildLayout(TypeSymbol typeSymbol, AbstractPhaseContext context)
        {
            Stack<TypeSymbol> typesToBuild = new Stack<TypeSymbol>();

            if (typeSymbol.IsUdonSharpInterface)
            {
                if (!_builtLayouts.ContainsKey(typeSymbol))
                    typesToBuild.Push(typeSymbol);
            }
            else
            {
                while (typeSymbol.BaseType != null && !_builtLayouts.ContainsKey(typeSymbol))
                {
                    typesToBuild.Push(typeSymbol);
                    if (typeSymbol == _udonSharpBehaviourType)
                        break;

                    typeSymbol = typeSymbol.BaseType;
                }
            }

            Dictionary<string, bool> networkCallableDedup = new();
            while (typesToBuild.Count > 0)
            {
                TypeSymbol currentBuildType = typesToBuild.Pop();

                Dictionary<string, int> idCounters;

                if (currentBuildType.BaseType != null &&
                    _builtLayouts.TryGetValue(currentBuildType.BaseType, out TypeLayout parentLayout))
                    idCounters = new Dictionary<string, int>(parentLayout.SymbolCounters);
                else
                    idCounters = new Dictionary<string, int>();

                Dictionary<MethodSymbol, MethodExportLayout> layouts =
                    new Dictionary<MethodSymbol, MethodExportLayout>();

                networkCallableDedup.Clear();
                
                foreach (Symbol symbol in currentBuildType.GetMembers(context))
                {
                    if (symbol is MethodSymbol methodSymbol && 
                        (methodSymbol.OverridenMethod == null || 
                         methodSymbol.OverridenMethod.ContainingType == _udonSharpBehaviourType || 
                         methodSymbol.OverridenMethod.ContainingType.IsExtern))
                    {
                        layouts.Add(methodSymbol, BuildMethodLayout(methodSymbol, idCounters, networkCallableDedup));
                    }
                }
                
                _builtLayouts.Add(currentBuildType, new TypeLayout(layouts, idCounters));
            }
        }

        /// <summary>
        /// Retrieves the method layout for a UdonSharpBehaviour method.
        /// This includes the method name, name of return variable, and name of parameter values.
        /// This is used internally by GetMethodLinkage in the EmitContext.
        /// This is also used when calling across UdonSharpBehaviours to determine what variables to set for parameters and such.
        /// </summary>
        /// <remarks>This method is thread safe and may be called safely from multiple Contexts at a time</remarks>
        public MethodExportLayout GetUsbMethodLayout(MethodSymbol method, AbstractPhaseContext context)
        {
            if (_udonSharpBehaviourType == null)
                _udonSharpBehaviourType = GetTypeSymbol(typeof(UdonSharpBehaviour), context);

            while (method.OverridenMethod != null &&
                   method.OverridenMethod.ContainingType != _udonSharpBehaviourType &&
                   !method.OverridenMethod.ContainingType.IsExtern)
                method = method.OverridenMethod;

            lock (_layoutLock)
            {
                if (_layouts.TryGetValue(method, out MethodExportLayout layout))
                    return layout;

                BuildLayout(method.ContainingType, context);

                return _layouts[method];
            }
        }

        public void BuildUdonBehaviourInheritanceLookup(IEnumerable<INamedTypeSymbol> rootTypes)
        {
            Dictionary<TypeSymbol, List<TypeSymbol>> inheritedTypeScratch = new Dictionary<TypeSymbol, List<TypeSymbol>>();

            TypeSymbol udonSharpBehaviourType = null;
            
            foreach (INamedTypeSymbol typeSymbol in rootTypes)
            {
                BindContext bindContext = new BindContext(this, typeSymbol, null);
                if (udonSharpBehaviourType == null)
                    udonSharpBehaviourType = bindContext.GetTypeSymbol(typeof(UdonSharpBehaviour));

                TypeSymbol rootTypeSymbol = bindContext.GetTypeSymbol(typeSymbol);

                TypeSymbol baseType = rootTypeSymbol.BaseType;

                while (baseType != udonSharpBehaviourType)
                {
                    if (!inheritedTypeScratch.TryGetValue(baseType, out List<TypeSymbol> inheritedTypeList))
                    {
                        inheritedTypeList = new List<TypeSymbol>();
                        inheritedTypeScratch.Add(baseType, inheritedTypeList);
                    }
                    
                    inheritedTypeList.Add(rootTypeSymbol);

                    baseType = baseType.BaseType;
                }
            }

            _inheritedTypes = new Dictionary<TypeSymbol, ImmutableArray<TypeSymbol>>();

            foreach (var typeLists in inheritedTypeScratch)
            {
                _inheritedTypes.Add(typeLists.Key, typeLists.Value.ToImmutableArray());
            }
        }

        public bool HasInheritedUdonSharpBehaviours(TypeSymbol baseType)
        {
            return _inheritedTypes.ContainsKey(baseType);
        }

        public ImmutableArray<TypeSymbol> GetInheritedTypes(TypeSymbol baseType)
        {
            if (_inheritedTypes.TryGetValue(baseType, out ImmutableArray<TypeSymbol> types))
                return types;
            
            return ImmutableArray<TypeSymbol>.Empty;
        }
    }
}

namespace UdonSharp.Compiler.Lowering
{
    internal sealed class ExtendedSyntaxLoweringDiagnostic
    {
        internal SyntaxNode Node { get; }
        internal string Message { get; }

        internal ExtendedSyntaxLoweringDiagnostic(SyntaxNode node, string message)
        {
            Node = node;
            Message = message;
        }
    }

    internal sealed class ExtendedSyntaxLoweringResult
    {
        internal SyntaxTree Tree { get; }
        internal ImmutableArray<ExtendedSyntaxLoweringDiagnostic> Diagnostics { get; }
        internal bool Changed { get; }

        internal ExtendedSyntaxLoweringResult(SyntaxTree tree,
            ImmutableArray<ExtendedSyntaxLoweringDiagnostic> diagnostics, bool changed)
        {
            Tree = tree;
            Diagnostics = diagnostics;
            Changed = changed;
        }
    }

    /// <summary>
    /// Erases extended syntax which has a deterministic Udon representation. Dynamic locals are
    /// narrowed from their initializer type, and immediate array Where/Select/ToArray pipelines are
    /// expanded into loops. Lambda captures remain ordinary reads of the surrounding variables in
    /// the generated loop, so no delegate or closure object reaches the U# binder.
    /// </summary>
    internal static class ExtendedSyntaxLowerer
    {
        internal static ExtendedSyntaxLoweringResult Rewrite(SyntaxTree tree,
            Func<ClassDeclarationSyntax, bool> shouldRewriteClass, SemanticModel semanticModel)
        {
            var rewriter = new Rewriter(shouldRewriteClass, semanticModel);
            SyntaxNode root = rewriter.Visit(tree.GetRoot());
            SyntaxTree rewrittenTree = rewriter.Changed
                ? tree.WithRootAndOptions(root, tree.Options)
                : tree;
            return new ExtendedSyntaxLoweringResult(rewrittenTree,
                rewriter.Diagnostics.ToImmutableArray(), rewriter.Changed);
        }

        private sealed class Rewriter : CSharpSyntaxRewriter
        {
            private readonly Func<ClassDeclarationSyntax, bool> _shouldRewriteClass;
            private readonly SemanticModel _semanticModel;
            private readonly HashSet<ISymbol> _narrowedDynamicLocals =
                new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            private bool _inTargetClass;
            private int _temporaryId;

            internal bool Changed { get; private set; }
            internal List<ExtendedSyntaxLoweringDiagnostic> Diagnostics { get; } =
                new List<ExtendedSyntaxLoweringDiagnostic>();

            internal Rewriter(Func<ClassDeclarationSyntax, bool> shouldRewriteClass,
                SemanticModel semanticModel)
            {
                _shouldRewriteClass = shouldRewriteClass;
                _semanticModel = semanticModel;
            }

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                bool previous = _inTargetClass;
                _inTargetClass = _shouldRewriteClass == null || _shouldRewriteClass(node);
                ClassDeclarationSyntax visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
                _inTargetClass = previous;
                return visited;
            }

            public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
            {
                if (_inTargetClass)
                    AddEscapingDelegateDiagnostic(node);
                return base.VisitSimpleLambdaExpression(node);
            }

            public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
            {
                if (_inTargetClass)
                    AddEscapingDelegateDiagnostic(node);
                return base.VisitParenthesizedLambdaExpression(node);
            }

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (node.SyntaxTree != _semanticModel.SyntaxTree)
                    return base.VisitIdentifierName(node);
                ISymbol symbol = _semanticModel.GetSymbolInfo(node).Symbol;
                if (_inTargetClass && _semanticModel.GetTypeInfo(node).Type?.TypeKind == TypeKind.Dynamic &&
                    !_narrowedDynamicLocals.Contains(symbol))
                    Diagnostics.Add(new ExtendedSyntaxLoweringDiagnostic(node,
                        "This dynamic value requires runtime dynamic dispatch; use a value whose single concrete type can be proven at build time."));
                return base.VisitIdentifierName(node);
            }

            public override SyntaxNode VisitGenericName(GenericNameSyntax node)
            {
                if (_inTargetClass && node.SyntaxTree == _semanticModel.SyntaxTree &&
                    _semanticModel.GetTypeInfo(node).Type is INamedTypeSymbol type &&
                    IsSpanType(type))
                    Diagnostics.Add(new ExtendedSyntaxLoweringDiagnostic(node,
                        "Only method-local array-backed Span<T>/ReadOnlySpan<T> values are supported; span fields, parameters, returns, captures, and unmanaged spans are not."));
                return base.VisitGenericName(node);
            }

            private void AddEscapingDelegateDiagnostic(LambdaExpressionSyntax lambda)
            {
                Diagnostics.Add(new ExtendedSyntaxLoweringDiagnostic(lambda,
                    "Escaping delegates are not supported in Udon; use an immediate supported array LINQ pipeline ending in ToArray()."));
            }

            public override SyntaxNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
            {
                if (!_inTargetClass || !node.Declaration.Type.IsKind(SyntaxKind.IdentifierName) ||
                    node.Declaration.Type.ToString() != "dynamic")
                    return base.VisitLocalDeclarationStatement(node);

                if (node.Declaration.Variables.Count != 1 ||
                    node.Declaration.Variables[0].Initializer == null)
                    return node;

                ExpressionSyntax initializer = node.Declaration.Variables[0].Initializer.Value;
                string variableName = node.Declaration.Variables[0].Identifier.ValueText;
                if (node.Parent is BlockSyntax containingBlock && containingBlock.DescendantNodes()
                        .Any(candidate =>
                            candidate is AssignmentExpressionSyntax assignment &&
                            assignment.Left is IdentifierNameSyntax assignedIdentifier &&
                            assignedIdentifier.Identifier.ValueText == variableName ||
                            candidate is PrefixUnaryExpressionSyntax prefix &&
                            prefix.Operand is IdentifierNameSyntax prefixIdentifier &&
                            prefixIdentifier.Identifier.ValueText == variableName ||
                            candidate is PostfixUnaryExpressionSyntax postfix &&
                            postfix.Operand is IdentifierNameSyntax postfixIdentifier &&
                            postfixIdentifier.Identifier.ValueText == variableName))
                    return node;
                ITypeSymbol initializerType = _semanticModel.GetTypeInfo(initializer).Type ??
                                              _semanticModel.GetTypeInfo(initializer).ConvertedType;
                if (!IsConcreteDynamicType(initializerType) ||
                    !HasOnlySafeDynamicUses(node, variableName))
                    return node;

                _narrowedDynamicLocals.Add(
                    _semanticModel.GetDeclaredSymbol(node.Declaration.Variables[0]));
                Changed = true;
                return node.WithDeclaration(node.Declaration.WithType(
                    SyntaxFactory.ParseTypeName(GetTypeSource(initializerType))
                        .WithTriviaFrom(node.Declaration.Type)));
            }

            public override SyntaxNode VisitBlock(BlockSyntax node)
            {
                if (!_inTargetClass)
                    return base.VisitBlock(node);

                var statements = new List<StatementSyntax>();
                var spans = new Dictionary<string, SpanInfo>(StringComparer.Ordinal);
                foreach (StatementSyntax statement in node.Statements)
                {
                    if (statement is LocalDeclarationStatementSyntax local &&
                        TryLowerArrayPipeline(local, out IEnumerable<StatementSyntax> lowered))
                    {
                        statements.AddRange(lowered);
                        Changed = true;
                    }
                    else if (statement is LocalDeclarationStatementSyntax spanLocal &&
                             TryLowerSpanDeclaration(spanLocal, spans,
                                 out IEnumerable<StatementSyntax> loweredSpan))
                    {
                        statements.AddRange(loweredSpan);
                        Changed = true;
                    }
                    else if (statement is LocalDeclarationStatementSyntax spanCopy &&
                             TryLowerSpanToArray(spanCopy, spans,
                                 out IEnumerable<StatementSyntax> loweredCopy))
                    {
                        statements.AddRange(loweredCopy);
                        Changed = true;
                    }
                    else if (statement is ExpressionStatementSyntax spanOperation &&
                             TryLowerSpanOperation(spanOperation, spans,
                                 out StatementSyntax loweredOperation))
                    {
                        statements.Add(loweredOperation);
                        Changed = true;
                    }
                    else
                    {
                        StatementSyntax spanRewritten = spans.Count == 0
                            ? statement
                            : (StatementSyntax)new SpanUseRewriter(spans, _semanticModel).Visit(statement);
                        statements.Add((StatementSyntax)Visit(spanRewritten));
                    }
                }

                return node.WithStatements(SyntaxFactory.List(statements));
            }

            private bool TryLowerSpanDeclaration(LocalDeclarationStatementSyntax declaration,
                IDictionary<string, SpanInfo> spans, out IEnumerable<StatementSyntax> loweredStatements)
            {
                loweredStatements = null;
                if (declaration.SyntaxTree != _semanticModel.SyntaxTree)
                    return false;
                if (declaration.Declaration.Variables.Count != 1)
                    return false;

                ITypeSymbol declaredType = _semanticModel.GetTypeInfo(declaration.Declaration.Type).Type;
                if (!(declaredType is INamedTypeSymbol spanType) || !IsSpanType(spanType))
                    return false;

                VariableDeclaratorSyntax variable = declaration.Declaration.Variables[0];
                if (variable.Initializer == null ||
                    !TryGetSpanBacking(variable.Initializer.Value, spans,
                        out string arrayExpression, out string offsetExpression, out string lengthExpression,
                        out string boundsOffsetExpression, out string boundsLengthExpression,
                        out List<StatementSyntax> argumentPrelude))
                    return false;

                int id = _temporaryId++;
                string prefix = $"__uspan_{id}_{variable.Identifier.ValueText}_";
                var info = new SpanInfo(prefix + "array", prefix + "offset", prefix + "length",
                    prefix + "backingLength",
                    GetTypeSource(spanType.TypeArguments[0]),
                    spanType.OriginalDefinition.ToDisplayString() == "System.ReadOnlySpan<T>",
                    _semanticModel.GetDeclaredSymbol(variable) as ILocalSymbol);
                spans[variable.Identifier.ValueText] = info;
                if (lengthExpression == null)
                    lengthExpression = $"{info.BackingLengthName} - {info.OffsetName}";
                else if (lengthExpression.StartsWith("__SPAN_REMAINDER__|", StringComparison.Ordinal))
                {
                    string[] parts = lengthExpression.Split('|');
                    lengthExpression = $"{parts[1]} - ({info.OffsetName} - {parts[2]})";
                }
                if (boundsLengthExpression == "__BACKING_ARRAY_LENGTH__")
                    boundsLengthExpression = info.BackingLengthName;
                var generated = new List<StatementSyntax>
                {
                    SyntaxFactory.ParseStatement($"{info.ElementType}[] {info.ArrayName} = {arrayExpression};"),
                    SyntaxFactory.ParseStatement(
                        $"int {info.BackingLengthName} = {info.ArrayName} == null ? 0 : {info.ArrayName}.Length;"),
                };
                generated.AddRange(argumentPrelude);
                generated.Add(SyntaxFactory.ParseStatement($"int {info.OffsetName} = {offsetExpression};"));
                generated.Add(SyntaxFactory.ParseStatement($"int {info.LengthName} = {lengthExpression};"));
                generated.Add(
                    SyntaxFactory.ParseStatement(
                        $"if ({info.OffsetName} < ({boundsOffsetExpression}) || {info.LengthName} < 0 || " +
                        $"{info.OffsetName} > ({boundsOffsetExpression}) + ({boundsLengthExpression}) - {info.LengthName}) " +
                        $"{{ UnityEngine.Debug.LogError(\"Span slice is outside the backing array.\"); " +
                        $"{info.OffsetName} = 0; {info.LengthName} = 0; }}"));
                loweredStatements = generated;
                return true;
            }

            private bool TryGetSpanBacking(ExpressionSyntax initializer,
                IDictionary<string, SpanInfo> spans, out string arrayExpression,
                out string offsetExpression, out string lengthExpression,
                out string boundsOffsetExpression, out string boundsLengthExpression,
                out List<StatementSyntax> argumentPrelude)
            {
                arrayExpression = null;
                offsetExpression = null;
                lengthExpression = null;
                boundsOffsetExpression = null;
                boundsLengthExpression = null;
                argumentPrelude = new List<StatementSyntax>();

                if (initializer is IdentifierNameSyntax existing &&
                    TryGetSpan(existing, spans, out SpanInfo existingInfo))
                {
                    arrayExpression = existingInfo.ArrayName;
                    offsetExpression = existingInfo.OffsetName;
                    lengthExpression = existingInfo.LengthName;
                    boundsOffsetExpression = existingInfo.OffsetName;
                    boundsLengthExpression = existingInfo.LengthName;
                    return true;
                }

                if (initializer is InvocationExpressionSyntax invocation &&
                    invocation.Expression is MemberAccessExpressionSyntax access)
                {
                    string operation = access.Name.Identifier.ValueText;
                    if (operation == "Slice" && access.Expression is IdentifierNameSyntax spanIdentifier &&
                        TryGetSpan(spanIdentifier, spans, out SpanInfo sliced) &&
                        IsFrameworkSpanSlice(invocation))
                    {
                        Dictionary<string, string> arguments = SpillSpanArguments(
                            invocation, spans, argumentPrelude);
                        string start = arguments.TryGetValue("start", out string startValue)
                            ? startValue : "0";
                        arrayExpression = sliced.ArrayName;
                        offsetExpression = $"{sliced.OffsetName} + ({start})";
                        lengthExpression = arguments.TryGetValue("length", out string lengthValue)
                            ? lengthValue
                            : $"__SPAN_REMAINDER__|{sliced.LengthName}|{sliced.OffsetName}";
                        boundsOffsetExpression = sliced.OffsetName;
                        boundsLengthExpression = sliced.LengthName;
                        return true;
                    }

                    if (operation == "AsSpan" &&
                        _semanticModel.GetTypeInfo(access.Expression).Type is IArrayTypeSymbol &&
                        IsFrameworkArrayAsSpan(invocation))
                    {
                        Dictionary<string, string> arguments = SpillSpanArguments(
                            invocation, spans, argumentPrelude);
                        string start = arguments.TryGetValue("start", out string startValue)
                            ? startValue : "0";
                        arrayExpression = access.Expression.ToString();
                        offsetExpression = start;
                        lengthExpression = arguments.TryGetValue("length", out string lengthValue)
                            ? lengthValue
                            : null;
                        boundsOffsetExpression = "0";
                        boundsLengthExpression = "__BACKING_ARRAY_LENGTH__";
                        return true;
                    }
                }

                if (_semanticModel.GetTypeInfo(initializer).Type is IArrayTypeSymbol)
                {
                    arrayExpression = initializer.ToString();
                    offsetExpression = "0";
                    lengthExpression = null;
                    boundsOffsetExpression = "0";
                    boundsLengthExpression = "__BACKING_ARRAY_LENGTH__";
                    return true;
                }

                return false;
            }

            private bool IsFrameworkArrayAsSpan(InvocationExpressionSyntax invocation)
            {
                if (!(_semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method))
                    return false;
                IMethodSymbol definition = method.ReducedFrom ?? method;
                if (definition.Name != "AsSpan" ||
                    definition.ContainingType?.ToDisplayString() != "System.MemoryExtensions")
                    return false;

                ImmutableArray<IParameterSymbol> parameters = method.Parameters;
                return parameters.Length == 0 ||
                       parameters.Length == 1 && parameters[0].Name == "start" &&
                       parameters[0].Type.SpecialType == SpecialType.System_Int32 ||
                       parameters.Length == 2 && parameters[0].Name == "start" &&
                       parameters[0].Type.SpecialType == SpecialType.System_Int32 &&
                       parameters[1].Name == "length" &&
                       parameters[1].Type.SpecialType == SpecialType.System_Int32;
            }

            private bool IsFrameworkSpanSlice(InvocationExpressionSyntax invocation)
            {
                if (!(_semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method))
                    return false;
                string containingType = method.ContainingType?.OriginalDefinition.ToDisplayString();
                return method.Name == "Slice" &&
                       (containingType == "System.Span<T>" ||
                        containingType == "System.ReadOnlySpan<T>");
            }

            private Dictionary<string, string> SpillSpanArguments(
                InvocationExpressionSyntax invocation, IDictionary<string, SpanInfo> spans,
                ICollection<StatementSyntax> prelude)
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                IMethodSymbol method = _semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                for (int index = 0; index < invocation.ArgumentList.Arguments.Count; index++)
                {
                    ArgumentSyntax argument = invocation.ArgumentList.Arguments[index];
                    string parameterName = argument.NameColon?.Name.Identifier.ValueText;
                    if (parameterName == null && method != null && index < method.Parameters.Length)
                        parameterName = method.Parameters[index].Name;
                    if (parameterName != "start" && parameterName != "length")
                        continue;

                    string temporary = $"__uspan_{_temporaryId++}_{parameterName}";
                    prelude.Add(SyntaxFactory.ParseStatement(
                        $"int {temporary} = {RewriteSpanExpression(argument.Expression, spans)};"));
                    values[parameterName] = temporary;
                }

                return values;
            }

            private string RewriteSpanExpression(ExpressionSyntax expression,
                IDictionary<string, SpanInfo> spans)
            {
                return ((ExpressionSyntax)new SpanUseRewriter(spans, _semanticModel)
                    .Visit(expression)).ToString();
            }

            private bool TryGetSpan(IdentifierNameSyntax identifier,
                IDictionary<string, SpanInfo> spans, out SpanInfo info)
            {
                if (!spans.TryGetValue(identifier.Identifier.ValueText, out info))
                    return false;
                return SymbolEqualityComparer.Default.Equals(
                    _semanticModel.GetSymbolInfo(identifier).Symbol, info.OriginalSymbol);
            }

            private bool TryLowerSpanToArray(LocalDeclarationStatementSyntax declaration,
                IDictionary<string, SpanInfo> spans, out IEnumerable<StatementSyntax> loweredStatements)
            {
                loweredStatements = null;
                if (declaration.Declaration.Variables.Count != 1)
                    return false;
                VariableDeclaratorSyntax variable = declaration.Declaration.Variables[0];
                if (!(variable.Initializer?.Value is InvocationExpressionSyntax invocation) ||
                    !(invocation.Expression is MemberAccessExpressionSyntax access) ||
                    access.Name.Identifier.ValueText != "ToArray" ||
                    !(access.Expression is IdentifierNameSyntax identifier) ||
                    !TryGetSpan(identifier, spans, out SpanInfo info))
                    return false;

                string copyIndex = $"__uspan_{_temporaryId++}_copy";
                loweredStatements = new[]
                {
                    SyntaxFactory.ParseStatement(
                        $"{info.ElementType}[] {variable.Identifier.ValueText} = new {info.ElementType}[{info.LengthName}];"),
                    SyntaxFactory.ParseStatement(
                        $"for (int {copyIndex} = 0; {copyIndex} < {info.LengthName}; {copyIndex}++) " +
                        $"{variable.Identifier.ValueText}[{copyIndex}] = {info.ArrayName}[{info.OffsetName} + {copyIndex}];"),
                };
                return true;
            }

            private bool TryLowerSpanOperation(ExpressionStatementSyntax statement,
                IDictionary<string, SpanInfo> spans, out StatementSyntax loweredStatement)
            {
                loweredStatement = null;
                if (!(statement.Expression is InvocationExpressionSyntax invocation) ||
                    !(invocation.Expression is MemberAccessExpressionSyntax access) ||
                    !(access.Expression is IdentifierNameSyntax identifier) ||
                    !TryGetSpan(identifier, spans, out SpanInfo info))
                    return false;

                string operation = access.Name.Identifier.ValueText;
                if (operation != "Clear" && operation != "Fill")
                    return false;
                if (info.IsReadOnly)
                {
                    Diagnostics.Add(new ExtendedSyntaxLoweringDiagnostic(statement,
                        "ReadOnlySpan<T> cannot be modified."));
                    return false;
                }
                if (operation == "Fill" && invocation.ArgumentList.Arguments.Count != 1)
                    return false;

                string index = $"__uspan_{_temporaryId++}_index";
                string valueName = $"__uspan_{_temporaryId++}_value";
                string value = operation == "Clear"
                    ? $"default({info.ElementType})"
                    : RewriteSpanExpression(invocation.ArgumentList.Arguments[0].Expression, spans);
                loweredStatement = SyntaxFactory.ParseStatement(
                    $"{{ {info.ElementType} {valueName} = {value}; " +
                    $"for (int {index} = 0; {index} < {info.LengthName}; {index}++) " +
                    $"{info.ArrayName}[{info.OffsetName} + {index}] = {valueName}; }}");
                return true;
            }

            private static bool IsSpanType(INamedTypeSymbol type)
            {
                string definition = type.OriginalDefinition.ToDisplayString();
                return definition == "System.Span<T>" || definition == "System.ReadOnlySpan<T>";
            }

            private sealed class SpanInfo
            {
                internal string ArrayName { get; }
                internal string OffsetName { get; }
                internal string LengthName { get; }
                internal string BackingLengthName { get; }
                internal string ElementType { get; }
                internal bool IsReadOnly { get; }
                // Original Roslyn identity prevents rewriting a different local with the same text name.
                internal ILocalSymbol OriginalSymbol { get; }

                internal SpanInfo(string arrayName, string offsetName, string lengthName,
                    string backingLengthName,
                    string elementType, bool isReadOnly, ILocalSymbol originalSymbol)
                {
                    ArrayName = arrayName;
                    OffsetName = offsetName;
                    LengthName = lengthName;
                    BackingLengthName = backingLengthName;
                    ElementType = elementType;
                    IsReadOnly = isReadOnly;
                    OriginalSymbol = originalSymbol;
                }
            }

            private sealed class SpanUseRewriter : CSharpSyntaxRewriter
            {
                private readonly IDictionary<string, SpanInfo> _spans;
                private readonly SemanticModel _semanticModel;

                internal SpanUseRewriter(IDictionary<string, SpanInfo> spans,
                    SemanticModel semanticModel)
                {
                    _spans = spans;
                    _semanticModel = semanticModel;
                }

                public override SyntaxNode VisitElementAccessExpression(ElementAccessExpressionSyntax node)
                {
                    if (node.Expression is IdentifierNameSyntax identifier &&
                        TryGetSpan(identifier, out SpanInfo info) &&
                        node.ArgumentList.Arguments.Count == 1)
                    {
                        string index = ((ExpressionSyntax)Visit(
                            node.ArgumentList.Arguments[0].Expression)).ToString();
                        return SyntaxFactory.ParseExpression(
                            $"{info.ArrayName}[{info.OffsetName} + ({index})]").WithTriviaFrom(node);
                    }

                    return base.VisitElementAccessExpression(node);
                }

                public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
                {
                    if (node.Expression is IdentifierNameSyntax identifier &&
                        node.Name.Identifier.ValueText == "Length" &&
                        TryGetSpan(identifier, out SpanInfo info))
                        return SyntaxFactory.IdentifierName(info.LengthName).WithTriviaFrom(node);

                    return base.VisitMemberAccessExpression(node);
                }

                private bool TryGetSpan(IdentifierNameSyntax identifier, out SpanInfo info)
                {
                    if (!_spans.TryGetValue(identifier.Identifier.ValueText, out info))
                        return false;
                    return SymbolEqualityComparer.Default.Equals(
                        _semanticModel.GetSymbolInfo(identifier).Symbol, info.OriginalSymbol);
                }
            }

            private bool TryLowerArrayPipeline(LocalDeclarationStatementSyntax declaration,
                out IEnumerable<StatementSyntax> loweredStatements)
            {
                loweredStatements = null;
                if (declaration.SyntaxTree != _semanticModel.SyntaxTree)
                    return false;
                if (declaration.Declaration.Variables.Count != 1)
                    return false;

                VariableDeclaratorSyntax variable = declaration.Declaration.Variables[0];
                if (!(variable.Initializer?.Value is InvocationExpressionSyntax toArray) ||
                    !(toArray.Expression is MemberAccessExpressionSyntax toArrayAccess) ||
                    toArrayAccess.Name.Identifier.ValueText != "ToArray" ||
                    toArray.ArgumentList.Arguments.Count != 0)
                    return false;

                if (!IsEnumerableMethod(toArray, "ToArray"))
                    return false;

                ExpressionSyntax source = toArrayAccess.Expression;
                var operations = new List<(string Name, LambdaExpressionSyntax Lambda)>();
                while (source is InvocationExpressionSyntax invocation &&
                       invocation.Expression is MemberAccessExpressionSyntax access &&
                       (access.Name.Identifier.ValueText == "Where" ||
                        access.Name.Identifier.ValueText == "Select") &&
                       invocation.ArgumentList.Arguments.Count == 1 &&
                       invocation.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax lambda &&
                       IsEnumerableMethod(invocation, access.Name.Identifier.ValueText))
                {
                    operations.Add((access.Name.Identifier.ValueText, lambda));
                    source = access.Expression;
                }

                if (operations.Count == 0 || !(_semanticModel.GetTypeInfo(source).Type is IArrayTypeSymbol sourceArray))
                    return false;
                operations.Reverse();

                bool hasProjection = false;
                foreach ((string name, LambdaExpressionSyntax _) in operations)
                {
                    if (name == "Select")
                        hasProjection = true;
                    else if (hasProjection)
                    {
                        Diagnostics.Add(new ExtendedSyntaxLoweringDiagnostic(declaration,
                            "Where after Select is not supported yet because Udon lowering must preserve single evaluation of projections."));
                        return false;
                    }
                }

                if (!(_semanticModel.GetTypeInfo(variable.Initializer.Value).Type is IArrayTypeSymbol resultArray))
                    return false;

                foreach ((string _, LambdaExpressionSyntax lambda) in operations)
                {
                    if (GetLambdaParameter(lambda) == null || !(lambda.Body is ExpressionSyntax))
                        return false;
                }

                int id = _temporaryId++;
                string prefix = $"__ulinq_{id}_";
                string sourceName = prefix + "source";
                string bufferName = prefix + "buffer";
                string countName = prefix + "count";
                string indexName = prefix + "index";
                string copyName = prefix + "copy";
                string sourceElementType = GetTypeSource(sourceArray.ElementType);
                string resultElementType = GetTypeSource(resultArray.ElementType);
                string currentExpression = $"{sourceName}[{indexName}]";
                var conditions = new List<string>();

                foreach ((string name, LambdaExpressionSyntax lambda) in operations)
                {
                    string parameter = GetLambdaParameter(lambda);
                    IParameterSymbol parameterSymbol = GetLambdaParameterSymbol(lambda);
                    ExpressionSyntax body = (ExpressionSyntax)lambda.Body;
                    string substituted = new LambdaParameterSubstitution(parameter, parameterSymbol,
                            _semanticModel, SyntaxFactory.ParseExpression(currentExpression))
                        .Visit(body).ToString();
                    if (name == "Where")
                        conditions.Add(substituted);
                    else
                        currentExpression = substituted;
                }

                var generated = new List<StatementSyntax>
                {
                    SyntaxFactory.ParseStatement($"{sourceElementType}[] {sourceName} = {source};"),
                    SyntaxFactory.ParseStatement($"{resultElementType}[] {bufferName} = new {resultElementType}[{sourceName}.Length];"),
                    SyntaxFactory.ParseStatement($"int {countName} = 0;")
                };

                string conditionSource = conditions.Count == 0
                    ? string.Empty
                    : $"if (!({string.Join(") || !(", conditions)})) continue;";
                generated.Add(SyntaxFactory.ParseStatement(
                    $"for (int {indexName} = 0; {indexName} < {sourceName}.Length; {indexName}++) " +
                    $"{{ {conditionSource} {bufferName}[{countName}] = {currentExpression}; {countName}++; }}"));
                generated.Add(SyntaxFactory.ParseStatement(
                    $"{resultElementType}[] {variable.Identifier.ValueText} = new {resultElementType}[{countName}];"));
                generated.Add(SyntaxFactory.ParseStatement(
                    $"for (int {copyName} = 0; {copyName} < {countName}; {copyName}++) " +
                    $"{variable.Identifier.ValueText}[{copyName}] = {bufferName}[{copyName}];"));
                loweredStatements = generated;
                return true;
            }

            private bool IsEnumerableMethod(InvocationExpressionSyntax invocation, string name)
            {
                return _semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
                       method.Name == name &&
                       method.ContainingType?.ToDisplayString() == "System.Linq.Enumerable";
            }

            private static string GetLambdaParameter(LambdaExpressionSyntax lambda)
            {
                if (lambda is SimpleLambdaExpressionSyntax simple)
                    return simple.Parameter.Identifier.ValueText;
                if (lambda is ParenthesizedLambdaExpressionSyntax parenthesized &&
                    parenthesized.ParameterList.Parameters.Count == 1)
                    return parenthesized.ParameterList.Parameters[0].Identifier.ValueText;
                return null;
            }

            private IParameterSymbol GetLambdaParameterSymbol(LambdaExpressionSyntax lambda)
            {
                ParameterSyntax parameter = lambda is SimpleLambdaExpressionSyntax simple
                    ? simple.Parameter
                    : (lambda as ParenthesizedLambdaExpressionSyntax)?.ParameterList.Parameters
                        .FirstOrDefault();
                return parameter == null ? null : _semanticModel.GetDeclaredSymbol(parameter);
            }

            private bool HasOnlySafeDynamicUses(LocalDeclarationStatementSyntax declaration,
                string variableName)
            {
                if (!(declaration.Parent is BlockSyntax block) ||
                    !(_semanticModel.GetDeclaredSymbol(declaration.Declaration.Variables[0]) is ILocalSymbol local))
                    return false;

                foreach (IdentifierNameSyntax reference in block.DescendantNodes()
                             .OfType<IdentifierNameSyntax>())
                {
                    if (reference.Identifier.ValueText != variableName ||
                        !SymbolEqualityComparer.Default.Equals(
                            _semanticModel.GetSymbolInfo(reference).Symbol, local))
                        continue;

                    if (!(reference.Parent is BinaryExpressionSyntax binary))
                        return false;
                    ExpressionSyntax other = binary.Left == reference ? binary.Right : binary.Left;
                    ITypeSymbol otherType = _semanticModel.GetTypeInfo(other).Type;
                    if (!IsConcreteDynamicType(otherType))
                        return false;
                }

                return true;
            }

            private static bool IsConcreteDynamicType(ITypeSymbol type)
            {
                if (type == null || type.TypeKind == TypeKind.Dynamic ||
                    type.TypeKind == TypeKind.Error || type.TypeKind == TypeKind.TypeParameter ||
                    type.IsAnonymousType)
                    return false;

                switch (type.SpecialType)
                {
                    case SpecialType.System_Boolean:
                    case SpecialType.System_Byte:
                    case SpecialType.System_Char:
                    case SpecialType.System_Double:
                    case SpecialType.System_Int16:
                    case SpecialType.System_Int32:
                    case SpecialType.System_Int64:
                    case SpecialType.System_SByte:
                    case SpecialType.System_Single:
                    case SpecialType.System_String:
                    case SpecialType.System_UInt16:
                    case SpecialType.System_UInt32:
                    case SpecialType.System_UInt64:
                        return true;
                    default:
                        return false;
                }
            }

            private static string GetTypeSource(ITypeSymbol type)
            {
                switch (type.SpecialType)
                {
                    case SpecialType.System_Boolean: return "bool";
                    case SpecialType.System_Byte: return "byte";
                    case SpecialType.System_Char: return "char";
                    case SpecialType.System_Double: return "double";
                    case SpecialType.System_Int16: return "short";
                    case SpecialType.System_Int32: return "int";
                    case SpecialType.System_Int64: return "long";
                    case SpecialType.System_SByte: return "sbyte";
                    case SpecialType.System_Single: return "float";
                    case SpecialType.System_String: return "string";
                    case SpecialType.System_UInt16: return "ushort";
                    case SpecialType.System_UInt32: return "uint";
                    case SpecialType.System_UInt64: return "ulong";
                    default:
                        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }

            private sealed class LambdaParameterSubstitution : CSharpSyntaxRewriter
            {
                private readonly string _parameter;
                private readonly IParameterSymbol _parameterSymbol;
                private readonly SemanticModel _semanticModel;
                private readonly ExpressionSyntax _replacement;

                internal LambdaParameterSubstitution(string parameter, IParameterSymbol parameterSymbol,
                    SemanticModel semanticModel, ExpressionSyntax replacement)
                {
                    _parameter = parameter;
                    _parameterSymbol = parameterSymbol;
                    _semanticModel = semanticModel;
                    _replacement = replacement;
                }

                public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
                {
                    bool isParameter = _parameterSymbol != null
                        ? SymbolEqualityComparer.Default.Equals(
                            _semanticModel.GetSymbolInfo(node).Symbol, _parameterSymbol)
                        : node.Identifier.ValueText == _parameter;
                    return isParameter
                        ? _replacement.WithTriviaFrom(node)
                        : base.VisitIdentifierName(node);
                }
            }
        }
    }

    internal sealed class AsyncSyntaxLoweringDiagnostic
    {
        internal SyntaxNode Node { get; }
        internal string Message { get; }

        internal AsyncSyntaxLoweringDiagnostic(SyntaxNode node, string message)
        {
            Node = node;
            Message = message;
        }
    }

    internal sealed class AsyncSyntaxLoweringResult
    {
        internal SyntaxTree Tree { get; }
        internal ImmutableArray<AsyncSyntaxLoweringDiagnostic> Diagnostics { get; }
        internal bool Changed { get; }

        internal AsyncSyntaxLoweringResult(SyntaxTree tree,
            ImmutableArray<AsyncSyntaxLoweringDiagnostic> diagnostics, bool changed)
        {
            Tree = tree;
            Diagnostics = diagnostics;
            Changed = changed;
        }
    }

    /// <summary>
    /// Performs the syntax portion of async lowering before the regular U# binder sees the program.
    /// This first vertical slice intentionally accepts only straight-line async-void methods using
    /// Task.Yield() and Task.Delay(int); unsupported shapes are diagnosed instead of leaking an
    /// AwaitExpression into the binder.
    /// </summary>
    internal static class AsyncSyntaxLowerer
    {
        internal static AsyncSyntaxLoweringResult Rewrite(SyntaxTree tree)
        {
            return Rewrite(tree, null, null);
        }

        internal static AsyncSyntaxLoweringResult Rewrite(SyntaxTree tree,
            Func<ClassDeclarationSyntax, bool> shouldRewriteClass)
        {
            return Rewrite(tree, shouldRewriteClass, null);
        }

        internal static AsyncSyntaxLoweringResult Rewrite(SyntaxTree tree,
            Func<ClassDeclarationSyntax, bool> shouldRewriteClass, SemanticModel semanticModel)
        {
            var rewriter = new AsyncMethodRewriter(shouldRewriteClass, semanticModel);
            SyntaxNode root = rewriter.Visit(tree.GetRoot());
            SyntaxTree rewrittenTree = rewriter.Changed
                ? tree.WithRootAndOptions(root, tree.Options)
                : tree;
            return new AsyncSyntaxLoweringResult(rewrittenTree,
                rewriter.Diagnostics.ToImmutableArray(), rewriter.Changed);
        }

        private sealed class AsyncMethodRewriter : CSharpSyntaxRewriter
        {
            private enum AwaitKind
            {
                Yield,
                Delay,
                StringLoad,
                ImageLoad,
                VideoLoad,
                VideoEnd,
                GpuReadback,
                Serialization,
                AvailableProducts,
                Purchases,
                ProductOwners,
            }

            private sealed class AwaitLowering
            {
                internal AwaitKind Kind;
                internal ExpressionSyntax DelayMilliseconds;
                internal InvocationExpressionSyntax Invocation;
            }

            private sealed class SdkAwaitDispatch
            {
                internal AwaitKind Kind;
                internal string StateName;
                internal string ResumeName;
                internal int ExpectedState;
                internal string PendingFieldName;
                internal FieldDeclarationSyntax PendingField;
                internal string AuxiliaryFieldName;
                internal FieldDeclarationSyntax AuxiliaryField;
                internal ExpressionSyntax ResultTarget;
            }

            private readonly Func<ClassDeclarationSyntax, bool> _shouldRewriteClass;
            private readonly SemanticModel _semanticModel;

            internal AsyncMethodRewriter(Func<ClassDeclarationSyntax, bool> shouldRewriteClass,
                SemanticModel semanticModel)
            {
                _shouldRewriteClass = shouldRewriteClass;
                _semanticModel = semanticModel;
            }

            internal List<AsyncSyntaxLoweringDiagnostic> Diagnostics { get; } =
                new List<AsyncSyntaxLoweringDiagnostic>();
            internal bool Changed { get; private set; }

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);
                if (_shouldRewriteClass != null && !_shouldRewriteClass(node))
                    return visited;

                var members = new List<MemberDeclarationSyntax>();
                var sdkDispatches = new List<SdkAwaitDispatch>();
                var reservedNames = new HashSet<string>(visited.Members.SelectMany(GetDeclaredMemberNames),
                    StringComparer.Ordinal);
                bool usesManualSync = UsesManualSync(node);

                for (int memberIndex = 0; memberIndex < visited.Members.Count; memberIndex++)
                {
                    MemberDeclarationSyntax member = visited.Members[memberIndex];
                    if (!(member is MethodDeclarationSyntax method) ||
                        !method.Modifiers.Any(SyntaxKind.AsyncKeyword))
                    {
                        members.Add(member);
                        continue;
                    }

                    MethodDeclarationSyntax semanticMethod = node.Members[memberIndex] as MethodDeclarationSyntax;
                    if (!TryRewriteMethod(method, semanticMethod, reservedNames, sdkDispatches, usesManualSync,
                            out MethodDeclarationSyntax entryMethod,
                            out FieldDeclarationSyntax stateField, out MethodDeclarationSyntax resumeMethod))
                    {
                        members.Add(member);
                        continue;
                    }

                    Changed = true;
                    reservedNames.Add(stateField.Declaration.Variables[0].Identifier.ValueText);
                    members.Add(stateField);
                    members.Add(entryMethod);
                    if (resumeMethod != null)
                    {
                        reservedNames.Add(resumeMethod.Identifier.ValueText);
                        members.Add(resumeMethod);
                    }
                }

                foreach (SdkAwaitDispatch dispatch in sdkDispatches)
                {
                    if (dispatch.PendingField != null)
                    {
                        reservedNames.Add(dispatch.PendingFieldName);
                        members.Add(dispatch.PendingField);
                    }
                    if (dispatch.AuxiliaryField != null)
                    {
                        reservedNames.Add(dispatch.AuxiliaryFieldName);
                        members.Add(dispatch.AuxiliaryField);
                    }
                }

                WeaveSdkCallbacks(members, sdkDispatches);

                return visited.WithMembers(SyntaxFactory.List(members));
            }

            private bool TryRewriteMethod(MethodDeclarationSyntax method,
                MethodDeclarationSyntax semanticMethod, HashSet<string> reservedNames,
                List<SdkAwaitDispatch> sdkDispatches, bool usesManualSync,
                out MethodDeclarationSyntax entryMethod, out FieldDeclarationSyntax stateField,
                out MethodDeclarationSyntax resumeMethod)
            {
                entryMethod = null;
                stateField = null;
                resumeMethod = null;
                IMethodSymbol semanticSymbol = _semanticModel != null && semanticMethod != null
                    ? _semanticModel.GetDeclaredSymbol(semanticMethod)
                    : null;

                if (!(method.ReturnType is PredefinedTypeSyntax returnType) ||
                    !returnType.Keyword.IsKind(SyntaxKind.VoidKeyword))
                    return Fail(method, "Only async void methods are supported by the current Udon async lowerer.");
                if (method.Modifiers.Any(SyntaxKind.StaticKeyword))
                    return Fail(method, "Static async Udon methods are not supported.");
                if (method.TypeParameterList != null)
                    return Fail(method.TypeParameterList, "Generic async Udon methods are not supported.");
                if (method.Body == null)
                    return Fail(method, "Async expression-bodied methods are not supported.");
                if (method.ParameterList.Parameters.Count != 0)
                    return Fail(method.ParameterList, "Async Udon methods with parameters are not supported yet.");
                if (method.Body.DescendantNodes().OfType<ReturnStatementSyntax>().Any())
                    return Fail(method.Body, "Explicit return statements in async Udon methods are not supported yet.");
                TryStatementSyntax awaitTry = method.Body.DescendantNodes().OfType<TryStatementSyntax>()
                    .FirstOrDefault(tryStatement => tryStatement.DescendantNodes().OfType<AwaitExpressionSyntax>().Any());
                if (awaitTry != null)
                    return Fail(awaitTry,
                        "await inside try/catch/finally is not supported by synchronous compiler-managed exception handling.");
                if (method.Body.DescendantNodes().Any(node =>
                        node is VariableDeclarationSyntax ||
                        node is DeclarationExpressionSyntax ||
                        node is SingleVariableDesignationSyntax ||
                        node is ForEachStatementSyntax ||
                        node is ForEachVariableStatementSyntax ||
                        node is CatchDeclarationSyntax))
                    return Fail(method.Body, "Locals in async Udon methods are not supported until frame hoisting is enabled.");

                var topLevelAwaits = method.Body.Statements
                    .OfType<ExpressionStatementSyntax>()
                    .Where(statement => statement.Expression is AwaitExpressionSyntax)
                    .ToArray();
                int allAwaitCount = method.Body.DescendantNodes().OfType<AwaitExpressionSyntax>().Count();
                if (topLevelAwaits.Length != allAwaitCount)
                    return Fail(method.Body, "Await must currently be a top-level statement in an async Udon method.");

                string methodKey = semanticSymbol == null
                    ? method.Identifier.ValueText
                    : $"{GetStableTypeHash(semanticSymbol.ContainingType):x16}_{method.Identifier.ValueText}";
                string stateName = $"__uasync_{methodKey}_state";
                string resumeName = $"__uasync_{methodKey}_resume";
                if (reservedNames.Contains(stateName) || reservedNames.Contains(resumeName))
                    return Fail(method,
                        $"Async method '{method.Identifier.ValueText}' conflicts with compiler-generated member names.");

                if (semanticSymbol != null && semanticSymbol.GetAttributes().Any(attribute =>
                    {
                        string attributeName = attribute.AttributeClass?.ToDisplayString();
                        return attributeName == "VRC.SDK3.UdonNetworkCalling.NetworkCallableAttribute" ||
                               attributeName == "UdonSharp.LCGPacketAttribute";
                    }))
                    return Fail(method,
                        $"Network callable method '{method.Identifier.ValueText}' cannot be async.");

                stateField = (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                    $"[System.NonSerialized] private int {stateName};");

                SyntaxTokenList modifiers = SyntaxFactory.TokenList(method.Modifiers
                    .Where(modifier => !modifier.IsKind(SyntaxKind.AsyncKeyword)));

                if (topLevelAwaits.Length == 0)
                {
                    entryMethod = method.WithModifiers(modifiers);
                    resumeMethod = null;
                    return true;
                }

                var segments = new List<List<StatementSyntax>> { new List<StatementSyntax>() };
                var awaitKinds = new List<AwaitLowering>();
                int sdkAwaitCount = 0;
                foreach (StatementSyntax statement in method.Body.Statements)
                {
                    if (!(statement is ExpressionStatementSyntax expressionStatement) ||
                        !(expressionStatement.Expression is AwaitExpressionSyntax awaitExpression))
                    {
                        segments[segments.Count - 1].Add(statement);
                        continue;
                    }

                    if (!TryClassifyAwait(awaitExpression.Expression, out AwaitLowering awaitLowering))
                        return Fail(awaitExpression,
                            "Only Task.Yield(), Task.Delay(positive constant milliseconds), and supported VRCAsync SDK helpers can currently be awaited in Udon.");

                    if (IsSdkAwait(awaitLowering.Kind))
                    {
                        if (awaitLowering.Kind == AwaitKind.Serialization && !usesManualSync)
                            return Fail(awaitExpression,
                                "RequestSerializationAsync requires [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)].");
                        if (sdkDispatches.Count + sdkAwaitCount > 0)
                            return Fail(awaitExpression,
                                "Only one pending VRChat SDK await is supported per behaviour in this callback-lowering slice.");
                        sdkAwaitCount++;
                    }

                    awaitKinds.Add(awaitLowering);
                    segments.Add(new List<StatementSyntax>());
                }

                var entryStatements = new List<StatementSyntax>
                {
                    SyntaxFactory.ParseStatement($"if ({stateName} != 0) return;"),
                    SyntaxFactory.ParseStatement($"{stateName} = -1;")
                };
                var methodReservedNames = new HashSet<string>(reservedNames, StringComparer.Ordinal);
                var methodSdkDispatches = new List<SdkAwaitDispatch>();
                entryStatements.AddRange(segments[0]);
                if (!AppendSchedule(entryStatements, stateName, resumeName, 1,
                        awaitKinds[0], methodReservedNames, methodSdkDispatches))
                    return false;
                entryMethod = method.WithModifiers(modifiers)
                    .WithBody(SyntaxFactory.Block(entryStatements));

                var sections = new List<SwitchSectionSyntax>();
                for (int i = 1; i < segments.Count; i++)
                {
                    var statements = new List<StatementSyntax>(segments[i]);
                    if (i < segments.Count - 1)
                    {
                        if (!AppendSchedule(statements, stateName, resumeName, i + 1,
                                awaitKinds[i], methodReservedNames, methodSdkDispatches))
                            return false;
                    }
                    else
                    {
                        statements.Add(SyntaxFactory.ParseStatement($"{stateName} = 0;"));
                        statements.Add(SyntaxFactory.ParseStatement("return;"));
                    }

                    sections.Add(SyntaxFactory.SwitchSection(
                        SyntaxFactory.SingletonList<SwitchLabelSyntax>(SyntaxFactory.CaseSwitchLabel(
                            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
                                SyntaxFactory.Literal(i)))),
                        SyntaxFactory.List(statements)));
                }

                resumeMethod = SyntaxFactory.MethodDeclaration(
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), resumeName)
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                    .WithBody(SyntaxFactory.Block(
                        SyntaxFactory.SwitchStatement(SyntaxFactory.IdentifierName(stateName))
                            .WithSections(SyntaxFactory.List(sections))));
                foreach (SdkAwaitDispatch dispatch in methodSdkDispatches)
                {
                    if (!string.IsNullOrEmpty(dispatch.PendingFieldName))
                        reservedNames.Add(dispatch.PendingFieldName);
                    if (!string.IsNullOrEmpty(dispatch.AuxiliaryFieldName))
                        reservedNames.Add(dispatch.AuxiliaryFieldName);
                    sdkDispatches.Add(dispatch);
                }
                return true;
            }

            private bool UsesManualSync(ClassDeclarationSyntax declaration)
            {
                INamedTypeSymbol type = _semanticModel?.GetDeclaredSymbol(declaration);
                AttributeData syncMode = type?.GetAttributes().FirstOrDefault(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == "UdonSharp.UdonBehaviourSyncModeAttribute");
                return syncMode != null && syncMode.ConstructorArguments.Length == 1 &&
                       syncMode.ConstructorArguments[0].Value is int value && value == 4;
            }

            private bool AppendSchedule(List<StatementSyntax> statements, string stateName,
                string resumeName, int nextState, AwaitLowering awaitLowering,
                HashSet<string> reservedNames, List<SdkAwaitDispatch> sdkDispatches)
            {
                if (awaitLowering.Kind == AwaitKind.Yield || awaitLowering.Kind == AwaitKind.Delay)
                {
                    statements.Add(SyntaxFactory.ParseStatement($"{stateName} = {nextState};"));
                    string schedule = awaitLowering.Kind == AwaitKind.Delay
                        ? $"SendCustomEventDelayedSeconds(nameof({resumeName}), ((float)({awaitLowering.DelayMilliseconds})) / 1000f);"
                        : $"SendCustomEventDelayedFrames(nameof({resumeName}), 1);";
                    statements.Add(SyntaxFactory.ParseStatement(schedule));
                    statements.Add(SyntaxFactory.ParseStatement("return;"));
                    return true;
                }

                string pendingSuffix = GetPendingSuffix(awaitLowering.Kind);
                string pendingFieldName = pendingSuffix == null ? null : $"{stateName}_{pendingSuffix}";
                if (pendingFieldName != null && reservedNames.Contains(pendingFieldName))
                    return Fail(awaitLowering.Invocation,
                        $"SDK await conflicts with compiler-generated member '{pendingFieldName}'.");

                FieldDeclarationSyntax pendingField = null;
                string auxiliaryFieldName = null;
                FieldDeclarationSyntax auxiliaryField = null;
                ExpressionSyntax resultTarget = null;
                if (awaitLowering.Kind == AwaitKind.StringLoad)
                {
                    ExpressionSyntax url = GetArgument(awaitLowering.Invocation, "url", 0);
                    if (url == null)
                        return Fail(awaitLowering.Invocation, "LoadStringAsync requires a URL argument.");
                    ArgumentSyntax resultArgument = GetArgumentSyntax(awaitLowering.Invocation, "result", 1);
                    if (resultArgument != null)
                    {
                        if (!TryGetStringResultTarget(resultArgument, out resultTarget))
                            return false;
                    }
                    pendingField = (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                        $"[System.NonSerialized] private VRC.SDKBase.VRCUrl {pendingFieldName};");
                    statements.Add(SyntaxFactory.ParseStatement($"{pendingFieldName} = {url};"));
                    statements.Add(SyntaxFactory.ParseStatement($"{stateName} = {nextState};"));
                    statements.Add(SyntaxFactory.ParseStatement(
                        $"VRC.SDK3.StringLoading.VRCStringDownloader.LoadUrl({pendingFieldName}, " +
                        "(VRC.Udon.Common.Interfaces.IUdonEventReceiver)this);"));
                }
                else if (awaitLowering.Kind == AwaitKind.ImageLoad)
                {
                    ExpressionSyntax downloader = GetArgument(awaitLowering.Invocation,
                        "downloader", 0);
                    ExpressionSyntax url = GetArgument(awaitLowering.Invocation, "url", 1);
                    ExpressionSyntax material = GetArgument(awaitLowering.Invocation,
                        "material", 2) ?? SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
                    ExpressionSyntax textureInfo = GetArgument(awaitLowering.Invocation,
                        "textureInfo", 3) ?? SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
                    if (downloader == null || url == null)
                        return Fail(awaitLowering.Invocation,
                            "LoadImageAsync requires downloader and URL arguments.");
                    pendingField = (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                        $"[System.NonSerialized] private VRC.SDK3.Image.IVRCImageDownload {pendingFieldName};");
                    statements.Add(SyntaxFactory.ParseStatement($"{stateName} = {nextState};"));
                    statements.Add(SyntaxFactory.ParseStatement(
                        $"{pendingFieldName} = ({downloader}).DownloadImage({url}, {material}, " +
                        $"(VRC.Udon.Common.Interfaces.IUdonEventReceiver)this, {textureInfo});"));
                }
                else if (awaitLowering.Kind == AwaitKind.VideoLoad)
                {
                    ExpressionSyntax player = GetArgument(awaitLowering.Invocation, "player", 0);
                    ExpressionSyntax url = GetArgument(awaitLowering.Invocation, "url", 1);
                    ExpressionSyntax playWhenReady = GetArgument(awaitLowering.Invocation,
                        "playWhenReady", 2) ?? SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression);
                    if (player == null || url == null)
                        return Fail(awaitLowering.Invocation, "LoadVideoAsync requires player and URL arguments.");
                    pendingField = (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                        $"[System.NonSerialized] private VRC.SDK3.Video.Components.Base.BaseVRCVideoPlayer {pendingFieldName};");
                    auxiliaryFieldName = $"{stateName}_playWhenReady";
                    if (reservedNames.Contains(auxiliaryFieldName))
                        return Fail(awaitLowering.Invocation,
                            $"SDK await conflicts with compiler-generated member '{auxiliaryFieldName}'.");
                    auxiliaryField = (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                        $"[System.NonSerialized] private bool {auxiliaryFieldName};");
                    statements.Add(SyntaxFactory.ParseStatement($"{pendingFieldName} = {player};"));
                    statements.Add(SyntaxFactory.ParseStatement($"{auxiliaryFieldName} = {playWhenReady};"));
                    statements.Add(SyntaxFactory.ParseStatement($"{stateName} = {nextState};"));
                    statements.Add(SyntaxFactory.ParseStatement($"{pendingFieldName}.LoadURL({url});"));
                }
                else if (awaitLowering.Kind == AwaitKind.VideoEnd)
                {
                    ExpressionSyntax player = GetArgument(awaitLowering.Invocation, "player", 0);
                    if (player == null)
                        return Fail(awaitLowering.Invocation, "WaitForVideoEndAsync requires a player argument.");
                    pendingField = (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                        $"[System.NonSerialized] private VRC.SDK3.Video.Components.Base.BaseVRCVideoPlayer {pendingFieldName};");
                    statements.Add(SyntaxFactory.ParseStatement($"{pendingFieldName} = {player};"));
                    statements.Add(SyntaxFactory.ParseStatement($"{stateName} = {nextState};"));
                }
                else if (awaitLowering.Kind == AwaitKind.GpuReadback)
                {
                    ExpressionSyntax source = GetArgument(awaitLowering.Invocation, "source", 0);
                    ExpressionSyntax mipIndex = GetArgument(awaitLowering.Invocation,
                        "mipIndex", 1) ?? SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
                        SyntaxFactory.Literal(0));
                    if (source == null)
                        return Fail(awaitLowering.Invocation, "RequestGPUReadbackAsync requires a source texture.");
                    pendingField = (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                        $"[System.NonSerialized] private VRC.SDK3.Rendering.VRCAsyncGPUReadbackRequest {pendingFieldName};");
                    statements.Add(SyntaxFactory.ParseStatement($"{stateName} = {nextState};"));
                    statements.Add(SyntaxFactory.ParseStatement(
                        $"{pendingFieldName} = VRC.SDK3.Rendering.VRCAsyncGPUReadback.Request({source}, {mipIndex}, " +
                        "(VRC.Udon.Common.Interfaces.IUdonEventReceiver)this);"));
                }
                else if (awaitLowering.Kind == AwaitKind.Serialization)
                {
                    statements.Add(SyntaxFactory.ParseStatement(
                        $"if (!VRC.SDKBase.Networking.IsOwner(gameObject)) " +
                        $"{{ UnityEngine.Debug.LogError(\"RequestSerializationAsync requires local ownership.\"); {stateName} = 0; return; }}"));
                    statements.Add(SyntaxFactory.ParseStatement($"{stateName} = {nextState};"));
                    statements.Add(SyntaxFactory.ParseStatement("RequestSerialization();"));
                }
                else if (awaitLowering.Kind == AwaitKind.AvailableProducts)
                {
                    statements.Add(SyntaxFactory.ParseStatement($"{stateName} = {nextState};"));
                    statements.Add(SyntaxFactory.ParseStatement(
                        "VRC.Economy.Store.ListAvailableProducts((VRC.Udon.Common.Interfaces.IUdonEventReceiver)this);"));
                }
                else if (awaitLowering.Kind == AwaitKind.Purchases)
                {
                    ExpressionSyntax player = GetArgument(awaitLowering.Invocation, "player", 0);
                    if (player == null)
                        return Fail(awaitLowering.Invocation, "ListPurchasesAsync requires a player argument.");
                    pendingField = (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                        $"[System.NonSerialized] private VRC.SDKBase.VRCPlayerApi {pendingFieldName};");
                    statements.Add(SyntaxFactory.ParseStatement($"{pendingFieldName} = {player};"));
                    statements.Add(SyntaxFactory.ParseStatement($"{stateName} = {nextState};"));
                    statements.Add(SyntaxFactory.ParseStatement(
                        $"VRC.Economy.Store.ListPurchases((VRC.Udon.Common.Interfaces.IUdonEventReceiver)this, {pendingFieldName});"));
                }
                else if (awaitLowering.Kind == AwaitKind.ProductOwners)
                {
                    ExpressionSyntax product = GetArgument(awaitLowering.Invocation, "product", 0);
                    if (product == null)
                        return Fail(awaitLowering.Invocation, "ListProductOwnersAsync requires a product argument.");
                    pendingField = (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
                        $"[System.NonSerialized] private VRC.Economy.IProduct {pendingFieldName};");
                    statements.Add(SyntaxFactory.ParseStatement($"{pendingFieldName} = {product};"));
                    statements.Add(SyntaxFactory.ParseStatement($"{stateName} = {nextState};"));
                    statements.Add(SyntaxFactory.ParseStatement(
                        $"VRC.Economy.Store.ListProductOwners((VRC.Udon.Common.Interfaces.IUdonEventReceiver)this, {pendingFieldName});"));
                }

                statements.Add(SyntaxFactory.ParseStatement("return;"));
                if (pendingFieldName != null)
                    reservedNames.Add(pendingFieldName);
                if (auxiliaryFieldName != null)
                    reservedNames.Add(auxiliaryFieldName);
                sdkDispatches.Add(new SdkAwaitDispatch
                {
                    Kind = awaitLowering.Kind,
                    StateName = stateName,
                    ResumeName = resumeName,
                    ExpectedState = nextState,
                    PendingFieldName = pendingFieldName,
                    PendingField = pendingField,
                    AuxiliaryFieldName = auxiliaryFieldName,
                    AuxiliaryField = auxiliaryField,
                    ResultTarget = resultTarget,
                });
                return true;
            }

            private bool TryClassifyAwait(ExpressionSyntax expression, out AwaitLowering awaitLowering)
            {
                awaitLowering = null;
                if (!(expression is InvocationExpressionSyntax invocation))
                    return false;

                if (_semanticModel != null)
                {
                    if (!(_semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method))
                        return false;

                    string containingType = method.ContainingType?.ToDisplayString();
                    if (containingType == "System.Threading.Tasks.Task" &&
                        method.Name == "Yield" && method.Parameters.Length == 0)
                    {
                        awaitLowering = new AwaitLowering { Kind = AwaitKind.Yield, Invocation = invocation };
                        return true;
                    }
                    if (containingType == "System.Threading.Tasks.Task" &&
                        method.Name == "Delay" && method.Parameters.Length == 1 &&
                        method.Parameters[0].Type.SpecialType == SpecialType.System_Int32)
                    {
                        Optional<object> constant = _semanticModel.GetConstantValue(
                            invocation.ArgumentList.Arguments[0].Expression);
                        if (!constant.HasValue || !(constant.Value is int milliseconds) || milliseconds <= 0)
                            return false;
                        awaitLowering = new AwaitLowering
                        {
                            Kind = AwaitKind.Delay,
                            DelayMilliseconds = invocation.ArgumentList.Arguments[0].Expression,
                            Invocation = invocation,
                        };
                        return true;
                    }

                    if (containingType == "UdonSharp.VRCAsync" && method.Name == "LoadStringAsync")
                    {
                        awaitLowering = new AwaitLowering
                            { Kind = AwaitKind.StringLoad, Invocation = invocation };
                        return true;
                    }
                    if (containingType == "UdonSharp.VRCAsync" && method.Name == "LoadImageAsync")
                    {
                        awaitLowering = new AwaitLowering
                            { Kind = AwaitKind.ImageLoad, Invocation = invocation };
                        return true;
                    }
                    if (containingType == "UdonSharp.VRCAsync" &&
                        TryGetSdkAwaitKind(method.Name, out AwaitKind sdkKind))
                    {
                        awaitLowering = new AwaitLowering { Kind = sdkKind, Invocation = invocation };
                        return true;
                    }

                    return false;
                }

                string methodName = invocation.Expression.ToString();
                if ((methodName == "Task.Yield" || methodName == "System.Threading.Tasks.Task.Yield") &&
                    invocation.ArgumentList.Arguments.Count == 0)
                {
                    awaitLowering = new AwaitLowering { Kind = AwaitKind.Yield, Invocation = invocation };
                    return true;
                }
                if ((methodName == "Task.Delay" || methodName == "System.Threading.Tasks.Task.Delay") &&
                    invocation.ArgumentList.Arguments.Count == 1)
                {
                    awaitLowering = new AwaitLowering
                    {
                        Kind = AwaitKind.Delay,
                        DelayMilliseconds = invocation.ArgumentList.Arguments[0].Expression,
                        Invocation = invocation,
                    };
                    return true;
                }

                return false;
            }

            private static bool TryGetSdkAwaitKind(string methodName, out AwaitKind kind)
            {
                switch (methodName)
                {
                    case "LoadStringAsync": kind = AwaitKind.StringLoad; return true;
                    case "LoadImageAsync": kind = AwaitKind.ImageLoad; return true;
                    case "LoadVideoAsync": kind = AwaitKind.VideoLoad; return true;
                    case "WaitForVideoEndAsync": kind = AwaitKind.VideoEnd; return true;
                    case "RequestGPUReadbackAsync": kind = AwaitKind.GpuReadback; return true;
                    case "RequestSerializationAsync": kind = AwaitKind.Serialization; return true;
                    case "ListAvailableProductsAsync": kind = AwaitKind.AvailableProducts; return true;
                    case "ListPurchasesAsync": kind = AwaitKind.Purchases; return true;
                    case "ListProductOwnersAsync": kind = AwaitKind.ProductOwners; return true;
                    default:
                        kind = default;
                        return false;
                }
            }

            private static bool IsSdkAwait(AwaitKind kind) =>
                kind != AwaitKind.Yield && kind != AwaitKind.Delay;

            private static string GetPendingSuffix(AwaitKind kind)
            {
                switch (kind)
                {
                    case AwaitKind.StringLoad: return "stringUrl";
                    case AwaitKind.ImageLoad: return "imageRequest";
                    case AwaitKind.VideoLoad:
                    case AwaitKind.VideoEnd: return "videoPlayer";
                    case AwaitKind.GpuReadback: return "gpuRequest";
                    case AwaitKind.Purchases: return "purchasesPlayer";
                    case AwaitKind.ProductOwners: return "ownersProduct";
                    default: return null;
                }
            }

            private static ExpressionSyntax GetArgument(InvocationExpressionSyntax invocation,
                string parameterName, int positionalIndex)
            {
                return GetArgumentSyntax(invocation, parameterName, positionalIndex)?.Expression;
            }

            private static ArgumentSyntax GetArgumentSyntax(InvocationExpressionSyntax invocation,
                string parameterName, int positionalIndex)
            {
                foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
                {
                    if (argument.NameColon?.Name.Identifier.ValueText == parameterName)
                        return argument;
                }

                if (positionalIndex < invocation.ArgumentList.Arguments.Count &&
                    invocation.ArgumentList.Arguments[positionalIndex].NameColon == null)
                    return invocation.ArgumentList.Arguments[positionalIndex];

                return null;
            }

            private bool TryGetStringResultTarget(ArgumentSyntax argument,
                out ExpressionSyntax resultTarget)
            {
                resultTarget = null;
                if (!argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
                    return Fail(argument, "LoadStringAsync result argument must use the 'out' keyword.");

                if (argument.Expression is IdentifierNameSyntax identifier)
                {
                    if (!IsInstanceField(identifier))
                        return Fail(argument,
                            "LoadStringAsync out result must be an instance behaviour field.");
                    resultTarget = SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.ThisExpression(), identifier.WithoutTrivia())
                        .WithTriviaFrom(identifier);
                    return true;
                }

                if (argument.Expression is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Expression is ThisExpressionSyntax && IsInstanceField(memberAccess))
                {
                    resultTarget = memberAccess;
                    return true;
                }

                return Fail(argument,
                    "LoadStringAsync out result must be an instance behaviour field.");
            }

            private bool IsInstanceField(ExpressionSyntax expression)
            {
                return _semanticModel?.GetSymbolInfo(expression).Symbol is IFieldSymbol field && !field.IsStatic;
            }

            private void WeaveSdkCallbacks(List<MemberDeclarationSyntax> members,
                List<SdkAwaitDispatch> dispatches)
            {
                foreach (SdkAwaitDispatch dispatch in dispatches)
                {
                    foreach (string callbackName in GetCallbackNames(dispatch.Kind))
                    {
                        string[] parameterTypes = GetCallbackParameterTypes(dispatch.Kind, callbackName);
                        string[] generatedParameterNames = GetCallbackParameterNames(dispatch.Kind, callbackName);
                        int existingIndex = members.FindIndex(member =>
                            member is MethodDeclarationSyntax method &&
                            method.Identifier.ValueText == callbackName);
                        string[] parameterNames = generatedParameterNames;
                        StatementSyntax dispatchStatement;
                        if (existingIndex >= 0)
                        {
                            var existing = (MethodDeclarationSyntax)members[existingIndex];
                            if (existing.ParameterList.Parameters.Count != parameterTypes.Length)
                            {
                                Diagnostics.Add(new AsyncSyntaxLoweringDiagnostic(existing,
                                    $"Legacy callback '{callbackName}' has the wrong SDK parameter count."));
                                continue;
                            }

                            parameterNames = existing.ParameterList.Parameters
                                .Select(parameter => parameter.Identifier.ValueText).ToArray();
                            dispatchStatement = CreateSdkDispatchStatement(dispatch, callbackName, parameterNames);
                            BlockSyntax body = existing.Body;
                            if (body == null && existing.ExpressionBody != null)
                                body = SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(
                                    existing.ExpressionBody.Expression));
                            if (body == null)
                            {
                                Diagnostics.Add(new AsyncSyntaxLoweringDiagnostic(existing,
                                    $"Legacy callback '{callbackName}' must have a method body."));
                                continue;
                            }
                            if (body.DescendantNodes().OfType<ReturnStatementSyntax>().Any())
                            {
                                Diagnostics.Add(new AsyncSyntaxLoweringDiagnostic(existing,
                                    $"Legacy callback '{callbackName}' cannot return early when it dispatches an SDK await continuation."));
                                continue;
                            }

                            members[existingIndex] = existing.WithExpressionBody(null)
                                .WithSemicolonToken(default)
                                .WithBody(body.AddStatements(dispatchStatement));
                        }
                        else
                        {
                            dispatchStatement = CreateSdkDispatchStatement(dispatch, callbackName, parameterNames);
                            var parameters = new List<ParameterSyntax>();
                            for (int i = 0; i < parameterTypes.Length; i++)
                            {
                                parameters.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameterNames[i]))
                                    .WithType(SyntaxFactory.ParseTypeName(parameterTypes[i])));
                            }
                            var generated = SyntaxFactory.MethodDeclaration(
                                    SyntaxFactory.ParseTypeName("void"), callbackName)
                                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                                    SyntaxFactory.Token(SyntaxKind.OverrideKeyword))
                                .WithParameterList(SyntaxFactory.ParameterList(
                                    SyntaxFactory.SeparatedList(parameters)))
                                .WithBody(SyntaxFactory.Block(dispatchStatement));
                            members.Add(generated);
                        }
                    }
                }
            }

            private static StatementSyntax CreateSdkDispatchStatement(SdkAwaitDispatch dispatch,
                string callbackName, string[] parameterNames)
            {
                string match;
                switch (dispatch.Kind)
                {
                    case AwaitKind.StringLoad:
                        match = $"{parameterNames[0]}.Url.Get() == {dispatch.PendingFieldName}.Get()";
                        break;
                    case AwaitKind.ImageLoad:
                    case AwaitKind.GpuReadback:
                        match = $"{parameterNames[0]} == {dispatch.PendingFieldName}";
                        break;
                    case AwaitKind.Purchases:
                        match = $"{parameterNames[1]} == {dispatch.PendingFieldName}";
                        break;
                    case AwaitKind.ProductOwners:
                        match = $"{parameterNames[0]} == {dispatch.PendingFieldName}";
                        break;
                    default:
                        match = "true";
                        break;
                }

                var actions = new List<string>();
                if (dispatch.Kind == AwaitKind.VideoLoad && callbackName == "OnVideoReady")
                    actions.Add($"if ({dispatch.AuxiliaryFieldName}) {dispatch.PendingFieldName}.Play();");
                if (dispatch.ResultTarget != null)
                    actions.Add($"{dispatch.ResultTarget} = {parameterNames[0]};");
                if (dispatch.AuxiliaryFieldName != null)
                    actions.Add($"{dispatch.AuxiliaryFieldName} = false;");
                if (dispatch.PendingFieldName != null)
                    actions.Add($"{dispatch.PendingFieldName} = null;");
                actions.Add($"{dispatch.ResumeName}();");
                return SyntaxFactory.ParseStatement(
                    $"if ({dispatch.StateName} == {dispatch.ExpectedState} && {match}) " +
                    $"{{ {string.Join(" ", actions)} }}");
            }

            private static string[] GetCallbackNames(AwaitKind kind)
            {
                switch (kind)
                {
                    case AwaitKind.StringLoad: return new[] { "OnStringLoadSuccess", "OnStringLoadError" };
                    case AwaitKind.ImageLoad: return new[] { "OnImageLoadSuccess", "OnImageLoadError" };
                    case AwaitKind.VideoLoad: return new[] { "OnVideoReady", "OnVideoError" };
                    case AwaitKind.VideoEnd: return new[] { "OnVideoEnd", "OnVideoError" };
                    case AwaitKind.GpuReadback: return new[] { "OnAsyncGpuReadbackComplete" };
                    case AwaitKind.Serialization: return new[] { "OnPostSerialization" };
                    case AwaitKind.AvailableProducts: return new[] { "OnListAvailableProducts" };
                    case AwaitKind.Purchases: return new[] { "OnListPurchases" };
                    case AwaitKind.ProductOwners: return new[] { "OnListProductOwners" };
                    default: return Array.Empty<string>();
                }
            }

            private static string[] GetCallbackParameterTypes(AwaitKind kind, string callbackName)
            {
                switch (kind)
                {
                    case AwaitKind.StringLoad:
                        return new[] { "VRC.SDK3.StringLoading.IVRCStringDownload" };
                    case AwaitKind.ImageLoad:
                        return new[] { "VRC.SDK3.Image.IVRCImageDownload" };
                    case AwaitKind.VideoLoad:
                    case AwaitKind.VideoEnd:
                        return callbackName == "OnVideoError"
                            ? new[] { "VRC.SDK3.Components.Video.VideoError" }
                            : Array.Empty<string>();
                    case AwaitKind.GpuReadback:
                        return new[] { "VRC.SDK3.Rendering.VRCAsyncGPUReadbackRequest" };
                    case AwaitKind.Serialization:
                        return new[] { "VRC.Udon.Common.SerializationResult" };
                    case AwaitKind.AvailableProducts:
                        return new[] { "VRC.Economy.IProduct[]" };
                    case AwaitKind.Purchases:
                        return new[] { "VRC.Economy.IProduct[]", "VRC.SDKBase.VRCPlayerApi" };
                    case AwaitKind.ProductOwners:
                        return new[] { "VRC.Economy.IProduct", "string[]" };
                    default:
                        return Array.Empty<string>();
                }
            }

            private static string[] GetCallbackParameterNames(AwaitKind kind, string callbackName)
            {
                switch (kind)
                {
                    case AwaitKind.StringLoad:
                    case AwaitKind.ImageLoad: return new[] { "result" };
                    case AwaitKind.VideoLoad:
                    case AwaitKind.VideoEnd:
                        return callbackName == "OnVideoError" ? new[] { "videoError" } : Array.Empty<string>();
                    case AwaitKind.GpuReadback: return new[] { "request" };
                    case AwaitKind.Serialization: return new[] { "result" };
                    case AwaitKind.AvailableProducts: return new[] { "products" };
                    case AwaitKind.Purchases: return new[] { "products", "player" };
                    case AwaitKind.ProductOwners: return new[] { "product", "owners" };
                    default: return Array.Empty<string>();
                }
            }

            private static ulong GetStableTypeHash(INamedTypeSymbol type)
            {
                const ulong offsetBasis = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                ulong hash = offsetBasis;
                string typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                foreach (char character in typeName)
                {
                    hash ^= character;
                    hash *= prime;
                }

                return hash;
            }

            private static IEnumerable<string> GetDeclaredMemberNames(MemberDeclarationSyntax member)
            {
                if (member is MethodDeclarationSyntax method)
                    return new[] { method.Identifier.ValueText };
                if (member is PropertyDeclarationSyntax property)
                    return new[] { property.Identifier.ValueText };
                if (member is EventDeclarationSyntax eventDeclaration)
                    return new[] { eventDeclaration.Identifier.ValueText };
                if (member is FieldDeclarationSyntax field)
                    return field.Declaration.Variables.Select(variable => variable.Identifier.ValueText);
                if (member is EventFieldDeclarationSyntax eventField)
                    return eventField.Declaration.Variables.Select(variable => variable.Identifier.ValueText);
                return Array.Empty<string>();
            }

            private bool Fail(SyntaxNode node, string message)
            {
                Diagnostics.Add(new AsyncSyntaxLoweringDiagnostic(node, message));
                return false;
            }
        }
    }

    internal enum CallbackCorrelation
    {
        ResultIdentity,
        UrlFifo,
        SingleFlight,
        Coalesced,
        PlayerFifo,
        ProductFifo,
    }

    internal sealed class SdkCallbackAdapter
    {
        internal string IntrinsicMethod { get; }
        internal string InitiatingMethod { get; }
        internal ImmutableArray<string> CompletionEvents { get; }
        internal string ResultType { get; }
        internal CallbackCorrelation Correlation { get; }
        internal bool MayRemainPending { get; }

        internal SdkCallbackAdapter(string intrinsicMethod, string initiatingMethod,
            string resultType, CallbackCorrelation correlation, bool mayRemainPending,
            params string[] completionEvents)
        {
            IntrinsicMethod = intrinsicMethod;
            InitiatingMethod = initiatingMethod;
            ResultType = resultType;
            Correlation = correlation;
            MayRemainPending = mayRemainPending;
            CompletionEvents = completionEvents.ToImmutableArray();
        }
    }

    internal static class SdkCallbackAdapterRegistry
    {
        internal static ImmutableArray<SdkCallbackAdapter> Adapters { get; } = ImmutableArray.Create(
            new SdkCallbackAdapter("LoadStringAsync", "VRCStringDownloader.LoadUrl",
                "VRC.SDK3.StringLoading.IVRCStringDownload", CallbackCorrelation.UrlFifo, false,
                "OnStringLoadSuccess", "OnStringLoadError"),
            new SdkCallbackAdapter("LoadImageAsync", "VRCImageDownloader.DownloadImage",
                "VRC.SDK3.Image.IVRCImageDownload", CallbackCorrelation.ResultIdentity, false,
                "OnImageLoadSuccess", "OnImageLoadError"),
            new SdkCallbackAdapter("LoadVideoAsync", "BaseVRCVideoPlayer.LoadURL",
                "UdonSharp.VRCVideoLoadResult", CallbackCorrelation.SingleFlight, false,
                "OnVideoReady", "OnVideoError"),
            new SdkCallbackAdapter("WaitForVideoEndAsync", "",
                "UdonSharp.VRCVideoPlaybackResult", CallbackCorrelation.SingleFlight, false,
                "OnVideoEnd", "OnVideoError"),
            new SdkCallbackAdapter("RequestGPUReadbackAsync", "VRCAsyncGPUReadback.Request",
                "VRC.SDK3.Rendering.VRCAsyncGPUReadbackRequest", CallbackCorrelation.SingleFlight, false,
                "OnAsyncGpuReadbackComplete"),
            new SdkCallbackAdapter("RequestSerializationAsync", "UdonBehaviour.RequestSerialization",
                "VRC.Udon.Common.SerializationResult", CallbackCorrelation.Coalesced, false,
                "OnPostSerialization"),
            new SdkCallbackAdapter("ListAvailableProductsAsync", "Store.ListAvailableProducts",
                "VRC.Economy.IProduct[]", CallbackCorrelation.SingleFlight, true,
                "OnListAvailableProducts"),
            new SdkCallbackAdapter("ListPurchasesAsync", "Store.ListPurchases",
                "UdonSharp.VRCPlayerPurchasesResult", CallbackCorrelation.PlayerFifo, true,
                "OnListPurchases"),
            new SdkCallbackAdapter("ListProductOwnersAsync", "Store.ListProductOwners",
                "UdonSharp.VRCProductOwnersResult", CallbackCorrelation.ProductFifo, true,
                "OnListProductOwners"));

        internal static bool TryGet(string methodName, out SdkCallbackAdapter adapter)
        {
            adapter = Adapters.FirstOrDefault(candidate =>
                string.Equals(candidate.IntrinsicMethod, methodName, StringComparison.Ordinal));
            return adapter != null;
        }
    }

    internal sealed class ExtendedLoweringPlan
    {
        internal static readonly ExtendedLoweringPlan Empty =
            new ExtendedLoweringPlan(ImmutableArray<SdkCallbackAdapter>.Empty, false);

        internal ImmutableArray<SdkCallbackAdapter> CallbackAdapters { get; }
        internal bool UsesAsyncState { get; }
        internal bool RequiresGeneratedState => UsesAsyncState || CallbackAdapters.Length > 0;

        internal ExtendedLoweringPlan(ImmutableArray<SdkCallbackAdapter> callbackAdapters, bool usesAsyncState)
        {
            CallbackAdapters = callbackAdapters;
            UsesAsyncState = usesAsyncState;
        }
    }

    internal static class LoweringPipeline
    {
        internal static bool PrepareSyntaxTrees(CompilationContext context, ModuleBinding[] modules,
            CSharpCompilation compilation)
        {
            bool changed = false;
            bool collectionChanged = false;
            foreach (ModuleBinding module in modules)
            {
                SemanticModel model = compilation.GetSemanticModel(module.tree);
                CollectionSyntaxLoweringResult result = CollectionSyntaxLowerer.Rewrite(module.tree,
                    declaration => model.GetDeclaredSymbol(declaration) is INamedTypeSymbol type &&
                                   type.IsUdonSharpBehaviour(), model);
                foreach (CollectionSyntaxLoweringDiagnostic diagnostic in result.Diagnostics)
                    context.AddDiagnostic(DiagnosticSeverity.Error, diagnostic.Node, diagnostic.Message);

                if (!result.Changed)
                    continue;

                module.tree = result.Tree;
                collectionChanged = true;
                changed = true;
            }

            if (collectionChanged)
                compilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(modules.Select(module => module.tree));

            bool extendedChanged = false;
            foreach (ModuleBinding module in modules)
            {
                SemanticModel model = compilation.GetSemanticModel(module.tree);
                ExtendedSyntaxLoweringResult result = ExtendedSyntaxLowerer.Rewrite(module.tree,
                    declaration => model.GetDeclaredSymbol(declaration) is INamedTypeSymbol type &&
                                   type.IsUdonSharpBehaviour(), model);
                foreach (ExtendedSyntaxLoweringDiagnostic diagnostic in result.Diagnostics)
                    context.AddDiagnostic(DiagnosticSeverity.Error, diagnostic.Node, diagnostic.Message);

                if (!result.Changed)
                    continue;

                module.tree = result.Tree;
                extendedChanged = true;
                changed = true;
            }

            if (extendedChanged)
                compilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(modules.Select(module => module.tree));

            foreach (ModuleBinding module in modules)
            {
                SemanticModel model = compilation.GetSemanticModel(module.tree);
                AsyncSyntaxLoweringResult result = AsyncSyntaxLowerer.Rewrite(module.tree,
                    declaration => model.GetDeclaredSymbol(declaration) is INamedTypeSymbol type &&
                                   type.IsUdonSharpBehaviour(), model);
                foreach (AsyncSyntaxLoweringDiagnostic diagnostic in result.Diagnostics)
                    context.AddDiagnostic(DiagnosticSeverity.Error, diagnostic.Node, diagnostic.Message);

                if (!result.Changed)
                    continue;

                module.tree = result.Tree;
                module.asyncSyntaxLowered = true;
                changed = true;
            }

            return changed;
        }

        internal static void Lower(CompilationContext context, ModuleBinding[] modules)
        {
            foreach (ModuleBinding module in modules)
                module.loweringPlan = CreatePlan(module);

            if (modules.Any(module => module.loweringPlan.RequiresGeneratedState))
            {
                int frameCapacity = UdonSharpSettings.GetSettings().asyncTaskFrameCapacity;
                if (frameCapacity < 1 || frameCapacity > 256)
                    context.AddDiagnostic(DiagnosticSeverity.Error, (Location)null,
                        "Async task frame capacity must be between 1 and 256.");
            }
        }

        private static ExtendedLoweringPlan CreatePlan(ModuleBinding module)
        {
            var root = module.tree.GetRoot();
            bool usesAsync = module.asyncSyntaxLowered ||
                             root.DescendantNodes().Any(node => node.IsKind(SyntaxKind.AwaitExpression)) ||
                             root.DescendantTokens().Any(token => token.IsKind(SyntaxKind.AsyncKeyword));
            var adapters = ImmutableArray.CreateBuilder<SdkCallbackAdapter>();

            foreach (var invocation in root.DescendantNodes()
                         .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
            {
                IMethodSymbol method = module.semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (method?.ContainingType?.ToDisplayString() != "UdonSharp.VRCAsync" ||
                    !SdkCallbackAdapterRegistry.TryGet(method.Name, out SdkCallbackAdapter adapter) ||
                    adapters.Contains(adapter))
                    continue;

                adapters.Add(adapter);
            }

            if (!usesAsync && adapters.Count == 0)
                return ExtendedLoweringPlan.Empty;

            return new ExtendedLoweringPlan(adapters.ToImmutable(), usesAsync);
        }
    }
}
