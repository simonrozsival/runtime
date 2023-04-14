// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler.ApplePlatforms;

public sealed class PreserveAttributeRootProvider : ICompilationRootProvider
{
    private EcmaModule _module;

    public PreserveAttributeRootProvider(EcmaModule module)
    {
        _module = module;
    }

    public void AddCompilationRoots(IRootingServiceProvider rootProvider)
    {
        MetadataReader reader = _module.MetadataReader;
        MetadataStringComparer comparer = reader.StringComparer;
        foreach (CustomAttributeHandle caHandle in reader.CustomAttributes)
        {
            CustomAttribute ca = reader.GetCustomAttribute(caHandle);
            if (ca.Parent.Kind != HandleKind.MethodDefinition)
                continue;

            if (!reader.GetAttributeNamespaceAndName(caHandle, out _, out StringHandle nameHandle))
                continue;

            // The PreserveAttribute doesn't need to be from any specific namespace.
            if (comparer.Equals(nameHandle, "PreserveAttribute"))
            {
                var attributeArguments = GetArguments(caHandle);
                var tmp = $"[Preserve(AllMembers = {attributeArguments.AllMembers}, Conditional = {attributeArguments.Conditional})]";
                switch (ca.Parent.Kind)
                {
                    case HandleKind.AssemblyDefinition:
                        // var assembly = (EcmaAssembly)_module.MetadataReader.GetAssembly(ca.Parent);
                        // TODO
                        // - check AllMembers: bool
                        // Console.WriteLine($"{tmp} assembly: {assembly}");
                        Console.WriteLine($"{tmp} assembly");
                        break;

                    case HandleKind.TypeDefinition:
                        var type = (EcmaType)_module.GetType(ca.Parent);
                        // TODO
                        // - check AllMembers: bool
                        // - check Conditional: bool
                        // rootProvider.AddReflectionRoot(type, reason); // ??
                        Console.WriteLine($"{tmp} type: {type}");
                        break;

                    case HandleKind.MethodDefinition:
                        var method = (EcmaMethod)_module.GetMethod(ca.Parent);
                        // TODO
                        // - check Conditional: bool
                        Console.WriteLine($"{tmp} method: {method}");
                        break;

                    case HandleKind.PropertyDefinition:
                        // var property = (EcmaProperty)_module.GetProperty(ca.Parent);
                        // TODO
                        // - check Conditional: bool
                        // Console.WriteLine($"{tmp} property: {property}");
                        Console.WriteLine($"{tmp} property");
                        break;

                    case HandleKind.FieldDefinition:
                        var field = (EcmaField)_module.GetField(ca.Parent);
                        // TODO
                        // - check Conditional: bool
                        Console.WriteLine($"{tmp} field: {field}");
                        break;

                    default:
                        // TODO log that this item can't have the attribute
                        Console.WriteLine($"{tmp} unsupported item: {ca.Parent.Kind}");
                        break;
                };
            }
        }
    }

    private sealed record PreserveAttributeArguments(bool AllMembers, bool Conditional) {}

    private PreserveAttributeArguments GetArguments(CustomAttributeHandle attributeHandle)
    {
        bool allMembers = false;
        bool conditional = false;

        var decodedValue = _module.MetadataReader.GetCustomAttribute(attributeHandle).DecodeValue(new CustomAttributeTypeProvider(_module));
        foreach (var argument in decodedValue.NamedArguments)
        {
            if (argument.Name == "AllMembers")
                allMembers = (bool)argument.Value;
            else if (argument.Name == "Conditional")
                allMembers = (bool)argument.Value;
        }

        return new PreserveAttributeArguments(allMembers, conditional);
    }
}
