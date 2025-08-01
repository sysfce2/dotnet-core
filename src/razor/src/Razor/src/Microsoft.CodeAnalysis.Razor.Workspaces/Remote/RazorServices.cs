﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.ExternalAccess.Razor;
using Microsoft.CodeAnalysis.Razor.Serialization.MessagePack.Resolvers;

namespace Microsoft.CodeAnalysis.Razor.Remote;

internal static class RazorServices
{
    // Internal for testing
    internal static readonly IEnumerable<(Type, Type?)> MessagePackServices =
        [
            (typeof(IRemoteLinkedEditingRangeService), null),
            (typeof(IRemoteTagHelperProviderService), null),
            (typeof(IRemoteSemanticTokensService), null),
            (typeof(IRemoteHtmlDocumentService), null),
            (typeof(IRemoteUriPresentationService), null),
            (typeof(IRemoteFoldingRangeService), null),
            (typeof(IRemoteDocumentHighlightService), null),
            (typeof(IRemoteAutoInsertService), null),
            (typeof(IRemoteFormattingService), null),
            (typeof(IRemoteSpellCheckService), null),
            (typeof(IRemoteInlineCompletionService), null),
            (typeof(IRemoteDebugInfoService), null),
            (typeof(IRemoteWrapWithTagService), null),
        ];

    // Internal for testing
    internal static readonly IEnumerable<(Type, Type?)> JsonServices =
        [
            (typeof(IRemoteClientInitializationService), null),
            (typeof(IRemoteGoToDefinitionService), null),
            (typeof(IRemoteHoverService), null),
            (typeof(IRemoteSignatureHelpService), null),
            (typeof(IRemoteInlayHintService), null),
            (typeof(IRemoteDocumentSymbolService), null),
            (typeof(IRemoteRenameService), null),
            (typeof(IRemoteGoToImplementationService), null),
            (typeof(IRemoteDiagnosticsService), null),
            (typeof(IRemoteCompletionService), null),
            (typeof(IRemoteCodeActionsService), null),
            (typeof(IRemoteFindAllReferencesService), null),
            (typeof(IRemoteMEFInitializationService), null),
        ];

    private const string ComponentName = "Razor";

    public static readonly RazorServiceDescriptorsWrapper Descriptors = new(
        ComponentName,
        featureDisplayNameProvider: GetFeatureDisplayName,
        additionalFormatters: [],
        additionalResolvers: TopLevelResolvers.All,
        interfaces: MessagePackServices);

    public static readonly RazorServiceDescriptorsWrapper JsonDescriptors = new(
        ComponentName, // Needs to match the above because so much of our ServiceHub infrastructure is convention based
        featureDisplayNameProvider: GetFeatureDisplayName,
        jsonConverters: RazorServiceDescriptorsWrapper.GetLspConverters(),
        interfaces: JsonServices);

    private static string GetFeatureDisplayName(string feature)
    {
        return $"Razor {feature} Feature";
    }
}
