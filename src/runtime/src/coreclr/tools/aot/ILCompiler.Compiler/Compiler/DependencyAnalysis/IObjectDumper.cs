// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ObjectData = ILCompiler.DependencyAnalysis.ObjectNode.ObjectData;

namespace ILCompiler.DependencyAnalysis
{
    public interface IObjectDumper
    {
        void DumpObjectNode(NodeFactory factory, ObjectNode node, ObjectData objectData);
        void ReportFoldedNode(NodeFactory factory, ObjectNode originalNode, ISymbolNode targetNode);
    }
}
