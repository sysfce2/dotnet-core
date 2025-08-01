namespace EmittedIL

open Xunit
open System.IO
open FSharp.Test
open FSharp.Test.Compiler

module SeqExpressionTailCalls =

    // Note: no realsignature variations are performed because these test cases do not involve any static initialization
    let verifyCompilation compilation =
        compilation
        |> withOptions [ "--test:EmitFeeFeeAs100001" ]
        |> asExe
        |> withNoOptimize
        |> withEmbeddedPdb
        |> withEmbedAllSource
        |> ignoreWarnings
        |> compile
        |> verifyILBaseline

    // SOURCE=SeqExpressionTailCalls01.fs SCFLAGS="-g --test:EmitFeeFeeAs100001 --optimize-" COMPILE_ONLY=1 POSTCMD="..\\CompareIL.cmd SeqExpressionTailCalls01.exe"	# SeqExpressionTailCalls01.fs -
    [<Theory; FileInlineData("SeqExpressionTailCalls01.fs")>]
    let ``SeqExpressionTailCalls01_fs`` compilation =
        compilation
        |> getCompilation
        |> verifyCompilation

    // SOURCE=SeqExpressionTailCalls02.fs SCFLAGS="-g --test:EmitFeeFeeAs100001 --optimize-" COMPILE_ONLY=1 POSTCMD="..\\CompareIL.cmd SeqExpressionTailCalls02.exe"	# SeqExpressionTailCalls02.fs -
    [<Theory; FileInlineData("SeqExpressionTailCalls02.fs")>]
    let ``SeqExpressionTailCalls02_fs`` compilation =
        compilation
        |> getCompilation
        |> verifyCompilation
