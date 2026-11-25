
using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using UdonSharp.LCGCompiler.Emit;
using UdonSharp.LCGCompiler.Symbols;
using UnityEngine;
using VRC.Udon.Serialization.OdinSerializer;

namespace UdonSharp.LCGCompiler
{
    [System.Serializable]
    public struct MethodDebugMarker
    {
        public int startInstruction;

        public int startLine;
        public int startChar;
    }

    [System.Serializable]
    public class MethodDebugInfo
    {
        public string methodName;
        public string containingFilePath;
        public uint methodStartAddress;
        public uint methodEndAddress;
        public List<MethodDebugMarker> debugMarkers;
    }
    
    [System.Serializable]
    public class AssemblyDebugInfo
    {
        [OdinSerialize]
        private List<MethodDebugInfo> _methodDebugInfos;

        [OdinSerialize]
        private Dictionary<uint, MethodDebugInfo> _callSiteLookup = new Dictionary<uint, MethodDebugInfo>();

        private MethodSymbol _currentMethod;
        private MethodDebugInfo _currentDebugInfo;

        internal void StartMethodEmit(MethodSymbol methodSymbol, EmitContext context)
        {
            _currentMethod = methodSymbol;
            _currentDebugInfo = new MethodDebugInfo();
            _currentDebugInfo.methodName = methodSymbol.Name;
            if (methodSymbol.RoslynSymbol.DeclaringSyntaxReferences.Length > 0)
            {
                _currentDebugInfo.containingFilePath = context.CompileContext
                    .TranslateLocationToFileName(methodSymbol.RoslynSymbol
                        .DeclaringSyntaxReferences.First().GetSyntax().GetLocation());
            }
            else
            {
                _currentDebugInfo.containingFilePath = "";
            }

            _currentDebugInfo.methodStartAddress = context.Module.CurrentAddress;
        }

        internal void FinalizeMethodEmit(EmitContext context)
        {
            _currentDebugInfo.methodEndAddress = context.Module.CurrentAddress;
            
            if (_methodDebugInfos == null)
                _methodDebugInfos = new List<MethodDebugInfo>();
            
            _methodDebugInfos.Add(_currentDebugInfo);
            _currentMethod = null;
            _currentDebugInfo = null;
            _currentSpan = null;
        }

        private MethodDebugMarker? _currentSpan;
        
        internal void UpdateSyntaxNode(SyntaxNode node, EmitContext context)
        {
            var nodeLocation = node.GetLocation().GetLineSpan().EndLinePosition;

            MethodDebugMarker currentMarker = _currentSpan ?? new MethodDebugMarker() { startInstruction = -1 };

            int currentAddress = (int)context.Module.CurrentAddress;

            if (currentMarker.startInstruction == -1)
            {
                currentMarker.startInstruction = currentAddress;
                currentMarker.startLine = nodeLocation.Line;
                currentMarker.startChar = nodeLocation.Character;

                _currentSpan = currentMarker;
            }
            else if (currentMarker.startLine != nodeLocation.Line ||
                     currentMarker.startChar != nodeLocation.Character)
            {
                if (_currentDebugInfo.debugMarkers == null)
                    _currentDebugInfo.debugMarkers = new List<MethodDebugMarker>();
                
                _currentDebugInfo.debugMarkers.Add(currentMarker);
                _currentSpan = null;
            }
        }

        internal void AddCallSite(uint callsite)
        {
            _callSiteLookup.Add(callsite, _currentDebugInfo);
        }
        
        internal void FinalizeAssemblyInfo()
        {
            
        }

        /// <summary>Copies this info into the stock UdonSharp type stored by UdonSharpEditorCache.</summary>
        internal UdonSharp.Compiler.AssemblyDebugInfo ToStock()
        {
            var converted = new Dictionary<MethodDebugInfo, UdonSharp.Compiler.MethodDebugInfo>();
            UdonSharp.Compiler.MethodDebugInfo Convert(MethodDebugInfo info)
            {
                if (info == null)
                    return null;
                if (converted.TryGetValue(info, out var stockInfo))
                    return stockInfo;

                return converted[info] = new UdonSharp.Compiler.MethodDebugInfo
                {
                    methodName = info.methodName,
                    containingFilePath = info.containingFilePath,
                    methodStartAddress = info.methodStartAddress,
                    methodEndAddress = info.methodEndAddress,
                    debugMarkers = info.debugMarkers?.Select(marker => new UdonSharp.Compiler.MethodDebugMarker
                    {
                        startInstruction = marker.startInstruction,
                        startLine = marker.startLine,
                        startChar = marker.startChar,
                    }).ToList(),
                };
            }

            var stock = new UdonSharp.Compiler.AssemblyDebugInfo();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(UdonSharp.Compiler.AssemblyDebugInfo).GetField(nameof(_methodDebugInfos), flags)
                .SetValue(stock, _methodDebugInfos?.Select(Convert).ToList());
            typeof(UdonSharp.Compiler.AssemblyDebugInfo).GetField(nameof(_callSiteLookup), flags)
                .SetValue(stock, _callSiteLookup?.ToDictionary(entry => entry.Key, entry => Convert(entry.Value)));
            return stock;
        }
        
        /// <summary>
        /// Gets the debug line span from a given program counter
        /// </summary>
        [PublicAPI]
        public void GetPositionFromProgramCounter(int programCounter, out string sourceFilePath, out string methodName, out int line, out int character)
        {
            sourceFilePath = null;
            methodName = null;
            line = 0;
            character = 0;
            
            if (_methodDebugInfos == null) return;

            foreach (MethodDebugInfo debugInfo in _methodDebugInfos)
            {
                if (programCounter >= debugInfo.methodStartAddress && programCounter < debugInfo.methodEndAddress)
                {
                    sourceFilePath = debugInfo.containingFilePath;
                    methodName = debugInfo.methodName;

                    if (debugInfo.debugMarkers == null || debugInfo.debugMarkers.Count == 0) return;

                    if (programCounter >= debugInfo.debugMarkers.Last().startInstruction)
                    {
                        line = debugInfo.debugMarkers.Last().startLine;
                        character = debugInfo.debugMarkers.Last().startChar;
                        return;
                    }

                    for (int i = 0; i < debugInfo.debugMarkers.Count - 1; ++i)
                    {
                        MethodDebugMarker currentMarker = debugInfo.debugMarkers[i];
                        MethodDebugMarker nextMarker = debugInfo.debugMarkers[i + 1];

                        if (programCounter >= currentMarker.startInstruction &&
                            programCounter < nextMarker.startInstruction)
                        {
                            line = currentMarker.startLine;
                            character = currentMarker.startChar;
                            return;
                        }
                    }

                    line = debugInfo.debugMarkers.Last().startLine;
                    character = debugInfo.debugMarkers.Last().startChar;
                }
            }
        }
    }
}
