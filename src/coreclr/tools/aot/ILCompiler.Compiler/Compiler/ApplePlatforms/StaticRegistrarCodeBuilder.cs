// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection.Metadata;

using Internal.TypeSystem.Ecma;

using ILCompiler.DependencyAnalysis;

namespace ILCompiler.ApplePlatforms;

public sealed class StaticRegistrarCodeBuilder
{
    private readonly string _headerFilePath;
    private readonly string _sourceFilePath;

    public StaticRegistrarCodeBuilder(string headerFilePath, string sourceFilePath)
    {
        _headerFilePath = headerFilePath;
        _sourceFilePath = sourceFilePath;
    }

    public static void AddMethod()
    {
        // TODO
    }

    public ObjectDumper ToObjectDumper() => new StaticRegistrarObjectDumper(this);

    private sealed class StaticRegistrarObjectDumper : ObjectDumper
    {
        private readonly StaticRegistrarCodeBuilder _builder;

        public StaticRegistrarObjectDumper(StaticRegistrarCodeBuilder builder)
        {
            _builder = builder;
        }

        protected override void DumpObjectNode(NodeFactory factory, ObjectNode node, ObjectNode.ObjectData objectData)
        {
            // TODO
        }

        internal override void Begin()
        {
            // TODO
        }

        internal override void End()
        {
            // TODO
        }
    }
}
