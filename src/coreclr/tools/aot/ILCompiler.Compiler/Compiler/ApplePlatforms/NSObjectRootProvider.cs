// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Internal.TypeSystem.Ecma;

namespace ILCompiler.ApplePlatforms;

public sealed class NSObjectRootProvider : ICompilationRootProvider
{
    private readonly EcmaModule _module;
    private readonly RuntimeModuleWrapper _runtimeModule;
    private readonly StaticRegistrarCodeBuilder _staticRegistrarCodeBuilder;

    public NSObjectRootProvider(
        EcmaModule module,
        RuntimeModuleWrapper runtimeModule,
        StaticRegistrarCodeBuilder staticRegistrarCodeBuilder)
    {
        _module = module;
        _runtimeModule = runtimeModule;
        _staticRegistrarCodeBuilder = staticRegistrarCodeBuilder;
    }

    public void AddCompilationRoots(IRootingServiceProvider rootProvider)
    {
        // TODO
    }

    // TODO we should change the naming from Xamarin's "product" to something else
    private bool IsProductModule => _module == _runtimeModule.Module;
}
