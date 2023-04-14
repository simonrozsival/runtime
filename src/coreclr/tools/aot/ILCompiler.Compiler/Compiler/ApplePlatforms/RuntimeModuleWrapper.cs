// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Runtime.CompilerServices;

using Internal.TypeSystem.Ecma;

namespace ILCompiler.ApplePlatforms;

public sealed class RuntimeModuleWrapper
{
    private const string MicrosoftIOSFileName = "Microsoft.iOS.dll";
    private const string MicrosoftMacOSFileName = "Microsoft.macOS.dll";

    public required EcmaModule Module { get; init; }

    public static RuntimeModuleWrapper FindRuntimeModule(CompilerTypeSystemContext typeSystemContext)
    {
        RuntimeModuleWrapper? runtimeModule = null;
        string? runtimeModuleFileName = null;

        foreach (var inputFile in typeSystemContext.InputFilePaths)
        {
            if (IsRuntimeAssemblyFile(inputFile.Value))
            {
                if (runtimeModule is not null)
                    throw new Exception($"Multiple runtime modules found: '{runtimeModuleFileName}' and '{inputFile.Value}'.");

                EcmaModule module = typeSystemContext.GetModuleFromPath(inputFile.Value);
                runtimeModule = new RuntimeModuleWrapper { Module = module };
                runtimeModuleFileName = inputFile.Value;
            }
        }

        if (runtimeModule is null)
            throw new Exception($"No runtime module found. Either {MicrosoftIOSFileName} or {MicrosoftMacOSFileName} is required.");

        return runtimeModule;

        static bool IsRuntimeAssemblyFile(string fileName)
            => fileName.EndsWith(MicrosoftIOSFileName, StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(MicrosoftMacOSFileName, StringComparison.OrdinalIgnoreCase);
    }
}
