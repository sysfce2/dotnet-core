﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis.ExternalAccess.Razor;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.VisualStudio.Razor.LanguageClient.Cohost;

public class CohostRenameEndpointTest(ITestOutputHelper testOutputHelper) : CohostEndpointTestBase(testOutputHelper)
{
    [Fact]
    public Task CSharp_Method()
        => VerifyRenamesAsync(
            input: """
                This is a Razor document.

                <h1>@MyMethod()</h1>

                @code
                {
                    public string MyMe$$thod()
                    {
                        return $"Hi from {nameof(MyMethod)}";
                    }
                }

                The end.
                """,
            newName: "CallThisFunction",
            expected: """
                This is a Razor document.
                
                <h1>@CallThisFunction()</h1>
                
                @code
                {
                    public string CallThisFunction()
                    {
                        return $"Hi from {nameof(CallThisFunction)}";
                    }
                }
                
                The end.
                """);

    [Theory]
    [InlineData("$$Component")]
    [InlineData("Com$$ponent")]
    [InlineData("Component$$")]
    public Task Component_StartTag(string startTag)
        => VerifyRenamesAsync(
            input: $"""
                This is a Razor document.

                <Component />

                <div>
                    <{startTag} />
                    <Component>
                    </Component>
                    <div>
                        <Component />
                        <Component>
                        </Component>
                    </div>
                </div>

                The end.
                """,
            additionalFiles: [
                (FilePath("Component.razor"), "")
            ],
            newName: "DifferentName",
            expected: """
                This is a Razor document.

                <DifferentName />
                
                <div>
                    <DifferentName />
                    <DifferentName>
                    </DifferentName>
                    <div>
                        <DifferentName />
                        <DifferentName>
                        </DifferentName>
                    </div>
                </div>

                The end.
                """,
            renames: [("Component.razor", "DifferentName.razor")]);

    [Theory]
    [InlineData("$$Component")]
    [InlineData("Com$$ponent")]
    [InlineData("Component$$")]
    public Task Component_EndTag(string endTag)
        => VerifyRenamesAsync(
            input: $"""
                This is a Razor document.

                <Component />

                <div>
                    <Component />
                    <Component>
                    </Component>
                    <div>
                        <Component />
                        <Component>
                        </{endTag}>
                    </div>
                </div>

                The end.
                """,
            additionalFiles: [
                (FilePath("Component.razor"), "")
            ],
            newName: "DifferentName",
            expected: """
                This is a Razor document.

                <DifferentName />

                <div>
                    <DifferentName />
                    <DifferentName>
                    </DifferentName>
                    <div>
                        <DifferentName />
                        <DifferentName>
                        </DifferentName>
                    </div>
                </div>

                The end.
                """,
            renames: [("Component.razor", "DifferentName.razor")]);

    [Fact]
    public Task Mvc()
        => VerifyRenamesAsync(
            input: """
                This is a Razor document.

                <Com$$ponent />

                The end.
                """,
            additionalFiles: [
                (FilePath("Component.razor"), "")
            ],
            newName: "DifferentName",
            expected: "",
            fileKind: RazorFileKind.Legacy);

    private async Task VerifyRenamesAsync(
        string input,
        string newName,
        string expected,
        RazorFileKind? fileKind = null,
        (string fileName, string contents)[]? additionalFiles = null,
        (string oldName, string newName)[]? renames = null)
    {
        TestFileMarkupParser.GetPosition(input, out var source, out var cursorPosition);
        var document = CreateProjectAndRazorDocument(source, fileKind, additionalFiles);
        var inputText = await document.GetTextAsync(DisposalToken);
        var position = inputText.GetPosition(cursorPosition);

        var requestInvoker = new TestHtmlRequestInvoker([(Methods.TextDocumentRenameName, null)]);

        var endpoint = new CohostRenameEndpoint(IncompatibleProjectService, RemoteServiceInvoker, requestInvoker);

        var renameParams = new RenameParams
        {
            Position = position,
            TextDocument = new TextDocumentIdentifier { DocumentUri = document.CreateDocumentUri() },
            NewName = newName,
        };

        var result = await endpoint.GetTestAccessor().HandleRequestAsync(renameParams, document, DisposalToken);

        if (expected.Length == 0)
        {
            Assert.True(renames is null or []);
            Assert.Null(result);
            return;
        }

        Assert.NotNull(result);

        if (result.DocumentChanges.AssumeNotNull().TryGetSecond(out var changes))
        {
            Assert.NotNull(renames);

            foreach (var change in changes)
            {
                if (change.TryGetThird(out var renameEdit))
                {
                    Assert.Contains(renames,
                        r => renameEdit.OldDocumentUri.GetRequiredParsedUri().GetDocumentFilePath().EndsWith(r.oldName) &&
                             renameEdit.NewDocumentUri.GetRequiredParsedUri().GetDocumentFilePath().EndsWith(r.newName));
                }
            }
        }

        var actual = ProcessRazorDocumentEdits(inputText, document.CreateUri(), result);

        AssertEx.EqualOrDiff(expected, actual);
    }

    private static string ProcessRazorDocumentEdits(SourceText inputText, Uri razorDocumentUri, WorkspaceEdit result)
    {
        Assert.True(result.TryGetTextDocumentEdits(out var textDocumentEdits));
        foreach (var textDocumentEdit in textDocumentEdits)
        {
            if (textDocumentEdit.TextDocument.DocumentUri.GetRequiredParsedUri() == razorDocumentUri)
            {
                foreach (var edit in textDocumentEdit.Edits)
                {
                    inputText = inputText.WithChanges(inputText.GetTextChange((TextEdit)edit));
                }
            }
        }

        return inputText.ToString();
    }
}
