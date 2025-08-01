﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.LanguageServer.ProjectSystem;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.Razor.LanguageServer.InlayHints;

public class InlayHintEndpointTest(ITestOutputHelper testOutput) : SingleServerDelegatingEndpointTestBase(testOutput)
{
    [Fact]
    public Task InlayHints()
        => VerifyInlayHintsAsync(
            input: """

                <div></div>

                @functions {
                    private void M(string thisIsMyString)
                    {
                        var {|int:x|} = 5;

                        var {|string:y|} = "Hello";

                        M({|thisIsMyString:"Hello"|});
                    }
                }

                """,
            toolTipMap: new Dictionary<string, string>
                {
                    { "int",            "struct System.Int32"            },
                    { "string",         "class System.String"            },
                    { "thisIsMyString", "(parameter) string thisIsMyStr" }
                },
            output: """

                <div></div>

                @functions {
                    private void M(string thisIsMyString)
                    {
                        int x = 5;

                        string y = "Hello";

                        M(thisIsMyString: "Hello");
                    }
                }

                """);

    [Fact]
    public Task InlayHints_ComponentAttributes()
        => VerifyInlayHintsAsync(
            input: """

                <div>
                    <InputText Value="_value" />
                    <InputText Value="@_value" />
                    <InputText Value="@(_value)" />
                </div>

                """,
            toolTipMap: new Dictionary<string, string>
            {
            },
            output: """

                <div>
                    <InputText Value="_value" />
                    <InputText Value="@_value" />
                    <InputText Value="@(_value)" />
                </div>

                """);

    [Theory]
    [InlineData(0, 0, 0, 20)]
    [InlineData(0, 0, 2, 0)]
    [InlineData(2, 0, 4, 0)]
    public async Task InlayHints_InvalidRange(int startLine, int starChar, int endLine, int endChar)
    {
        var input = """
            <div></div>
            """;
        var razorFilePath = "C:/path/to/file.razor";
        var codeDocument = CreateCodeDocument(input, filePath: razorFilePath);

        await using var languageServer = await CreateLanguageServerAsync(codeDocument, razorFilePath);

        var service = new InlayHintService(DocumentMappingService);

        var endpoint = new InlayHintEndpoint(service, languageServer);

        var request = new InlayHintParams()
        {
            TextDocument = new VSTextDocumentIdentifier
            {
                DocumentUri = new(new Uri(razorFilePath))
            },
            Range = LspFactory.CreateRange(startLine, starChar, endLine, endChar)
        };
        Assert.True(DocumentContextFactory.TryCreate(request.TextDocument, out var documentContext));
        var requestContext = CreateRazorRequestContext(documentContext);

        // Act
        var hints = await endpoint.HandleRequestAsync(request, requestContext, DisposalToken);

        // Assert
        Assert.Null(hints);
    }

    private async Task VerifyInlayHintsAsync(string input, Dictionary<string, string> toolTipMap, string output)
    {
        TestFileMarkupParser.GetSpans(input, out input, out ImmutableDictionary<string, ImmutableArray<TextSpan>> spansDict);
        var razorFilePath = "C:/path/to/file.razor";
        var codeDocument = CreateCodeDocument(input, filePath: razorFilePath);

        await using var languageServer = await CreateLanguageServerAsync(codeDocument, razorFilePath);

        var service = new InlayHintService(DocumentMappingService);

        var endpoint = new InlayHintEndpoint(service, languageServer);
        var resolveEndpoint = new InlayHintResolveEndpoint(service, languageServer);

        var request = new InlayHintParams()
        {
            TextDocument = new VSTextDocumentIdentifier
            {
                DocumentUri = new(new Uri(razorFilePath))
            },
            Range = LspFactory.CreateRange(0, 0, codeDocument.Source.Text.Lines.Count, 0)
        };
        Assert.True(DocumentContextFactory.TryCreate(request.TextDocument, out var documentContext));
        var requestContext = CreateRazorRequestContext(documentContext);

        // Act
        var hints = await endpoint.HandleRequestAsync(request, requestContext, DisposalToken);

        // Assert
        Assert.NotNull(hints);
        Assert.Equal(spansDict.Values.Count(), hints.Length);

        var sourceText = SourceText.From(input);
        foreach (var hint in hints)
        {
            // Because our test input data can't have colons in the input, but parameter info returned from Roslyn does, we have to strip them off.
            var label = hint.Label.First.TrimEnd(':');
            Assert.True(spansDict.TryGetValue(label, out var spans), $"Expected {label} to be in test provided markers");

            var span = Assert.Single(spans);
            var expectedRange = sourceText.GetRange(span);
            // Inlay hints only have a position, so we ignore the end of the range that comes from the test input
            Assert.Equal(expectedRange.Start, hint.Position);

            // This looks weird, but its what we have to do to satisfy the compiler :)
            string? expectedTooltip = null;
            Assert.True(toolTipMap?.TryGetValue(label, out expectedTooltip));
            Assert.NotNull(expectedTooltip);

            var resolvedHint = await resolveEndpoint.HandleRequestAsync(hint, requestContext, DisposalToken);
            Assert.NotNull(resolvedHint);
            Assert.NotNull(resolvedHint.ToolTip);

            if (resolvedHint.ToolTip.Value.TryGetFirst(out var plainTextTooltip))
            {
                Assert.Equal(expectedTooltip, plainTextTooltip);
            }
            else if (resolvedHint.ToolTip.Value.TryGetSecond(out var markupTooltip))
            {
                Assert.Contains(expectedTooltip, markupTooltip.Value);
            }
        }

        // To validate edits, we have to collect them all together, and apply them backwards
        var edits = hints
            .SelectMany(h => h.TextEdits ?? [])
            .OrderByDescending(e => e.Range.Start.Line)
            .ThenByDescending(e => e.Range.Start.Character)
            .ToArray();
        foreach (var edit in edits)
        {
            var change = sourceText.GetTextChange(edit);
            sourceText = sourceText.WithChanges(change);
        }

        AssertEx.EqualOrDiff(output, sourceText.ToString());
    }
}
