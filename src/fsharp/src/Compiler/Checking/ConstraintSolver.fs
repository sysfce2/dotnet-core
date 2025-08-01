// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.


//-------------------------------------------------------------------------
// Incremental type inference constraint solving.  
//
// Primary constraints are:
//   - type equations        ty1 = ty2
//   - subtype inequations   ty1 :> ty2
//   - trait constraints     tyname: (static member op_Addition: 'a * 'b -> 'c)
//
// Plus some other constraints inherited from .NET generics.
// 
// The constraints are immediately processed into a normal form, in particular
//   - type equations on inference parameters: 'tp = ty
//   - type inequations on inference parameters: 'tp :> ty
//   - other constraints on inference parameters
//
// The state of the inference engine is kept in imperative mutations to inference
// type variables.
//
// The use of the normal form allows the state of the inference engine to 
// be queried for type-directed name resolution, type-directed overload 
// resolution and when generating warning messages.
//
// The inference engine can be used in 'undo' mode to implement
// can-unify predicates used in method overload resolution and trait constraint
// satisfaction.
//
// The two main principles are:
//   1. Ensure any solution that is found is sound (no logic is skipped), 
//   2. Because of method overloading and SRTP constraints and other constructs, processing of
//      constraints is algorithmic and must proceed in a definite, fixed order.
//      Once we start doing resolutions in a particular order we must keep doing them
//      in the same order.
//
// There is little use of back-tracking/undo or "retry" in the constraint solver, except in the
// limited case ofs of SRTP solving and method overloading, and some other adhoc limited cases
// like checking for "printf" format strings.  As a result there are cases involving
// method overloading and SRTP that the solver "can't solve". This is intentional and by-design.
//------------------------------------------------------------------------- 

module internal FSharp.Compiler.ConstraintSolver

open FSharp.Compiler.Text.Range
open Internal.Utilities.Collections
open Internal.Utilities.Library
open Internal.Utilities.Library.Extras
open Internal.Utilities.Rational

open FSharp.Compiler 
open FSharp.Compiler.AbstractIL 
open FSharp.Compiler.AccessibilityLogic
open FSharp.Compiler.AttributeChecking
open FSharp.Compiler.DiagnosticsLogger
open FSharp.Compiler.Features
open FSharp.Compiler.Import
open FSharp.Compiler.InfoReader
open FSharp.Compiler.Infos
open FSharp.Compiler.MethodCalls
open FSharp.Compiler.NameResolution
open FSharp.Compiler.Syntax
open FSharp.Compiler.Syntax.PrettyNaming
open FSharp.Compiler.SyntaxTreeOps
open FSharp.Compiler.TcGlobals
open FSharp.Compiler.Text
open FSharp.Compiler.TypedTree
open FSharp.Compiler.TypedTreeBasics
open FSharp.Compiler.TypedTreeOps
open FSharp.Compiler.TypeHierarchy
open FSharp.Compiler.TypeRelations

#if !NO_TYPEPROVIDERS
open FSharp.Compiler.TypeProviders
#endif

//-------------------------------------------------------------------------
// Generate type variables and record them in within the scope of the
// compilation environment, which currently corresponds to the scope
// of the constraint resolution carried out by type checking.
//------------------------------------------------------------------------- 
   
let compgenId = mkSynId Range.range0 unassignedTyparName

let NewCompGenTypar (kind, rigid, staticReq, dynamicReq, error) = 
    Construct.NewTypar(kind, rigid, SynTypar(compgenId, staticReq, true), error, dynamicReq, [], false, false) 
    
let AnonTyparId m = mkSynId m unassignedTyparName

let NewAnonTypar (kind, m, rigid, var, dyn) = 
    Construct.NewTypar (kind, rigid, SynTypar(AnonTyparId m, var, true), false, dyn, [], false, false)
    
let NewNamedInferenceMeasureVar (_m, rigid, var, id) = 
    Construct.NewTypar(TyparKind.Measure, rigid, SynTypar(id, var, false), false, TyparDynamicReq.No, [], false, false) 

let NewInferenceMeasurePar () =
    NewCompGenTypar (TyparKind.Measure, TyparRigidity.Flexible, TyparStaticReq.None, TyparDynamicReq.No, false)

let NewErrorTypar () =
    NewCompGenTypar (TyparKind.Type, TyparRigidity.Flexible, TyparStaticReq.None, TyparDynamicReq.No, true)
    
let NewErrorType () =
    mkTyparTy (NewErrorTypar ())

let FreshenTypar (g: TcGlobals) rigid (tp: Typar) =
    let clearStaticReq = g.langVersion.SupportsFeature LanguageFeature.InterfacesWithAbstractStaticMembers
    let staticReq = if clearStaticReq then TyparStaticReq.None else tp.StaticReq
    let dynamicReq = if rigid = TyparRigidity.Rigid then TyparDynamicReq.Yes else TyparDynamicReq.No
    NewCompGenTypar (tp.Kind, rigid, staticReq, dynamicReq, false)

// QUERY: should 'rigid' ever really be 'true'? We set this when we know
// we are going to have to generalize a typar, e.g. when implementing a 
// abstract generic method slot. But we later check the generalization 
// condition anyway, so we could get away with a non-rigid typar. This 
// would sort of be cleaner, though give errors later. 
let FreshenAndFixupTypars g m rigid fctps tinst tpsorig =
    let tps = tpsorig |> List.map (FreshenTypar g rigid)
    let renaming, tinst = FixupNewTypars m fctps tinst tpsorig tps
    tps, renaming, tinst

let FreshenTypeInst g m tpsorig =
    FreshenAndFixupTypars g m TyparRigidity.Flexible [] [] tpsorig

let FreshMethInst g m fctps tinst tpsorig =
    FreshenAndFixupTypars g m TyparRigidity.Flexible fctps tinst tpsorig

let FreshenMethInfo m (minfo: MethInfo) =
    let _, _, tpTys = FreshMethInst minfo.TcGlobals m (minfo.GetFormalTyparsOfDeclaringType m) minfo.DeclaringTypeInst minfo.FormalMethodTypars
    tpTys

//-------------------------------------------------------------------------
// Unification of types: solve/record equality constraints
// Subsumption of types: solve/record subtyping constraints
//------------------------------------------------------------------------- 

/// Information about the context of a type equation.
[<RequireQualifiedAccess>]
type ContextInfo =

    /// No context was given.
    | NoContext

    /// The type equation comes from an IF expression.
    | IfExpression of range

    /// The type equation comes from an omitted else branch.
    | OmittedElseBranch of range

    /// The type equation comes from a type check of the result of an else branch.
    | ElseBranchResult of range

    /// The type equation comes from the verification of record fields.
    | RecordFields

    /// The type equation comes from the verification of a tuple in record fields.
    | TupleInRecordFields

    /// The type equation comes from a list or array constructor
    | CollectionElement of bool * range

    /// The type equation comes from a return in a computation expression.

    | ReturnInComputationExpression

    /// The type equation comes from a yield in a computation expression.
    | YieldInComputationExpression

    /// The type equation comes from a runtime type test.
    | RuntimeTypeTest of bool

    /// The type equation comes from an downcast where a upcast could be used.
    | DowncastUsedInsteadOfUpcast of bool

    /// The type equation comes from a return type of a pattern match clause (not the first clause).
    | FollowingPatternMatchClause of range

    /// The type equation comes from a pattern match guard.
    | PatternMatchGuard of range

    /// The type equation comes from a sequence expression.
    | SequenceExpression of TType

/// Captures relevant information for a particular failed overload resolution.
type OverloadInformation = 
    {
        methodSlot: CalledMeth<Expr>
        infoReader : InfoReader
        error: exn
    }

/// Cases for overload resolution failure that exists in the implementation of the compiler.
type OverloadResolutionFailure =
  | NoOverloadsFound  of
      methodName: string *
      candidates: OverloadInformation list *
      cx: TraitConstraintInfo option
  | PossibleCandidates of 
      methodName: string *
      candidates: OverloadInformation list *
      cx: TraitConstraintInfo option

type OverallTy = 
    /// Each branch of the expression must have the type indicated
    | MustEqual of TType

    /// Each branch of the expression must convert to the type indicated
    | MustConvertTo of isMethodArg: bool * ty: TType

    /// Represents a point where no subsumption/widening is possible
    member x.Commit = 
        match x with 
        | MustEqual ty -> ty
        | MustConvertTo (_, ty) -> ty

exception ConstraintSolverTupleDiffLengths of displayEnv: DisplayEnv * contextInfo: ContextInfo * TType list * TType list * range * range

exception ConstraintSolverInfiniteTypes of displayEnv: DisplayEnv * contextInfo: ContextInfo * TType * TType * range * range

exception ConstraintSolverTypesNotInEqualityRelation of displayEnv: DisplayEnv * TType * TType * range * range * ContextInfo

exception ConstraintSolverTypesNotInSubsumptionRelation of displayEnv: DisplayEnv * argTy: TType * paramTy: TType * callRange: range * parameterRange: range

exception ConstraintSolverMissingConstraint of displayEnv: DisplayEnv * Typar * TyparConstraint * range * range

exception ConstraintSolverNullnessWarningEquivWithTypes of DisplayEnv * TType * TType * NullnessInfo * NullnessInfo * range  * range 

exception ConstraintSolverNullnessWarningWithTypes of DisplayEnv * TType * TType * NullnessInfo * NullnessInfo * range  * range 

exception ConstraintSolverNullnessWarningWithType of DisplayEnv * TType * NullnessInfo * range  * range 

exception ConstraintSolverNullnessWarning of string * range * range 

exception ConstraintSolverError of string * range * range

exception ErrorFromApplyingDefault of tcGlobals: TcGlobals * displayEnv: DisplayEnv * Typar * TType * error: exn * range: range

exception ErrorFromAddingTypeEquation of tcGlobals: TcGlobals * displayEnv: DisplayEnv * expectedTy: TType * actualTy: TType * error: exn * range: range

exception ErrorsFromAddingSubsumptionConstraint of tcGlobals: TcGlobals * displayEnv: DisplayEnv * expectedTy: TType * actualTy: TType * error: exn * ctxtInfo: ContextInfo * parameterRange: range

exception ErrorFromAddingConstraint of displayEnv: DisplayEnv * error: exn * range: range

exception UnresolvedOverloading of displayEnv: DisplayEnv * callerArgs: CallerArgs<Expr> * failure: OverloadResolutionFailure * range: range

exception UnresolvedConversionOperator of displayEnv: DisplayEnv * TType * TType * range

type TcValF = ValRef -> ValUseFlag -> TType list -> range -> Expr * TType

type ConstraintSolverState = 
    { 
      g: TcGlobals

      amap: ImportMap 

      InfoReader: InfoReader

      /// The function used to freshen values we encounter during trait constraint solving
      TcVal: TcValF

      /// This table stores all unsolved, ungeneralized trait constraints, indexed by free type variable.
      /// That is, there will be one entry in this table for each free type variable in 
      /// each outstanding, unsolved, ungeneralized trait constraint. Constraints are removed from the table and resolved 
      /// each time a solution to an index variable is found. 
      mutable ExtraCxs: HashMultiMap<Stamp, TraitConstraintInfo * range>

      /// Checks to run after all inference is complete, but before defaults are applied and internal unknowns solved
      PostInferenceChecksPreDefaults: ResizeArray<unit -> unit>

      /// Checks to run after all inference is complete.
      PostInferenceChecksFinal: ResizeArray<unit -> unit>

      WarnWhenUsingWithoutNullOnAWithNullTarget: string option
    }

    static member New(g, amap, infoReader, tcVal) = 
        { g = g 
          amap = amap 
          ExtraCxs = HashMultiMap(10, HashIdentity.Structural)
          InfoReader = infoReader
          TcVal = tcVal
          PostInferenceChecksPreDefaults = ResizeArray()
          PostInferenceChecksFinal = ResizeArray()
          WarnWhenUsingWithoutNullOnAWithNullTarget = None } 

    member this.PushPostInferenceCheck (preDefaults, check) =
        if preDefaults then
            this.PostInferenceChecksPreDefaults.Add check
        else
            this.PostInferenceChecksFinal.Add check

    member this.PopPostInferenceCheck (preDefaults) =
        if preDefaults then
            this.PostInferenceChecksPreDefaults.RemoveAt(this.PostInferenceChecksPreDefaults.Count-1)
        else
            this.PostInferenceChecksFinal.RemoveAt(this.PostInferenceChecksPreDefaults.Count-1)

    member this.GetPostInferenceChecksPreDefaults() =
        this.PostInferenceChecksPreDefaults.ToArray() :> seq<_>

    member this.GetPostInferenceChecksFinal() =
        this.PostInferenceChecksFinal.ToArray() :> seq<_>

type ConstraintSolverEnv = 
    { 
      SolverState: ConstraintSolverState

      eContextInfo: ContextInfo

      // Is this speculative, with a trace allowing undo, and trial method overload resolution 
      IsSpeculativeForMethodOverloading: bool

      // Can this ignore the 'must support null' constraint, e.g. in a mutable assignment scenario
      IsSupportsNullFlex: bool

      /// Indicates that when unifying ty1 = ty2, only type variables in ty1 may be solved. Constraints
      /// can't be added to type variables in ty2
      MatchingOnly: bool

      /// Indicates that special errors on unresolved SRTP constraint overloads may be generated. When
      /// these are caught they result in postponed constraints.
      ErrorOnFailedMemberConstraintResolution: bool

      /// During MatchingOnly constraint solving, marks additional type variables as
      /// rigid, preventing constraints flowing to those type variables.
      ExtraRigidTypars: Zset<Typar>

      m: range

      EquivEnv: TypeEquivEnv

      DisplayEnv: DisplayEnv
    }

    member csenv.InfoReader = csenv.SolverState.InfoReader

    member csenv.g = csenv.SolverState.g

    member csenv.amap = csenv.SolverState.amap
    
    override csenv.ToString() = "<ConstraintSolverEnv> @ " + csenv.m.ToString()

let MakeConstraintSolverEnv contextInfo css m denv = 
    { SolverState = css
      m = m
      eContextInfo = contextInfo
      MatchingOnly = false
      ErrorOnFailedMemberConstraintResolution = false
      EquivEnv = TypeEquivEnv.EmptyIgnoreNulls
      DisplayEnv = denv
      IsSpeculativeForMethodOverloading = false
      IsSupportsNullFlex = false
      ExtraRigidTypars = emptyFreeTypars
    }

/// Check whether a type variable occurs in the r.h.s. of a type, e.g. to catch
/// infinite equations such as 
///    'a = 'a list
let rec occursCheck g un ty = 
    match stripTyEqns g ty with 
    | TType_ucase(_, l)
    | TType_app (_, l, _) 
    | TType_anon(_, l)
    | TType_tuple (_, l) -> List.exists (occursCheck g un) l
    | TType_fun (domainTy, rangeTy, _) -> occursCheck g un domainTy || occursCheck g un rangeTy
    | TType_var (r, _) ->  typarEq un r 
    | TType_forall (_, tau) -> occursCheck g un tau
    | _ -> false 

//-------------------------------------------------------------------------
// Predicates on types
//------------------------------------------------------------------------- 

/// Some additional solutions are forced prior to generalization (permitWeakResolution=true).  These are, roughly speaking, rules
/// for binary-operand constraints arising from constructs such as "1.0 + x" where "x" is an unknown type. The constraint here
/// involves two type parameters - one for the left, and one for the right.  The left is already known to be Double.
/// In this situation (and in the absence of other evidence prior to generalization), constraint solving forces an assumption that 
/// the right is also Double - this is "weak" because there is only weak evidence for it.
///
/// permitWeakResolution also applies to resolutions of multi-type-variable constraints via method overloads.  Method overloading gets applied even if
/// only one of the two type variables is known.
///
/// During code gen we run with permitWeakResolution on, but we only apply it where one of the argument types for the built-in constraint resolution is
/// a variable type.
type PermitWeakResolution = 
    | Yes
    | No
    member x.Permit = match x with Yes -> true | No -> false

let rec isNativeIntegerTy g ty =
    typeEquivAux EraseMeasures g g.nativeint_ty ty || 
    typeEquivAux EraseMeasures g g.unativeint_ty ty ||
    (isEnumTy g ty && isNativeIntegerTy g (underlyingTypeOfEnumTy g ty))

let rec IsIntegerOrIntegerEnumTy g ty =
    isSignedIntegerTy g ty || 
    isUnsignedIntegerTy g ty || 
    (isEnumTy g ty && IsIntegerOrIntegerEnumTy g (underlyingTypeOfEnumTy g ty))
    
let isStringTy g ty = typeEquiv g g.string_ty ty 

let isCharTy g ty = typeEquiv g g.char_ty ty 

let isBoolTy g ty = typeEquiv g g.bool_ty ty 

let IsNonDecimalNumericOrIntegralEnumType g ty = IsIntegerOrIntegerEnumTy g ty || isFpTy g ty

let IsNumericOrIntegralEnumType g ty = IsNonDecimalNumericOrIntegralEnumType g ty || isDecimalTy g ty

let IsRelationalType g ty = isNumericType g ty || isStringTy g ty || isCharTy g ty || isBoolTy g ty

let IsCharOrStringType g ty = isCharTy g ty || isStringTy g ty

/// Checks the argument type for a built-in solution to an op_Addition, op_Subtraction or op_Modulus constraint.
let IsAddSubModType nm g ty = IsNumericOrIntegralEnumType g ty || (nm = "op_Addition" && IsCharOrStringType g ty) || (nm = "op_Subtraction" && isCharTy g ty)

/// Checks the argument type for a built-in solution to a bitwise operator constraint
let IsBitwiseOpType g ty = IsIntegerOrIntegerEnumTy g ty || (isEnumTy g ty)

/// Check the other type in a built-in solution for a binary operator.
/// For weak resolution, require a relevant primitive on one side.
/// For strong resolution, a variable type is permitted.
let IsBinaryOpOtherArgType g permitWeakResolution ty = 
    match permitWeakResolution with 
    | PermitWeakResolution.No -> 
        not (isTyparTy g ty) 

    | PermitWeakResolution.Yes -> true

/// Checks the argument type for a built-in solution to a get_Sign constraint.
let IsSignType g ty =
    isSignedIntegerTy g ty || isFpTy g ty || isDecimalTy g ty

type TraitConstraintSolution = 
    | TTraitUnsolved
    | TTraitBuiltIn
    | TTraitSolved of minfo: MethInfo * minst: TypeInst * staticTyOpt: TType option
    | TTraitSolvedRecdProp of fieldInfo: RecdFieldInfo * isSetProp: bool
    | TTraitSolvedAnonRecdProp of anonRecdTypeInfo: AnonRecdTypeInfo * typeInst: TypeInst * index: int

let BakedInTraitConstraintNames =
    [ "op_Division" ; "op_Multiply"; "op_Addition" 
      "op_Equality" ; "op_Inequality"; "op_GreaterThan" ; "op_LessThan"; "op_LessThanOrEqual"; "op_GreaterThanOrEqual"
      "op_Subtraction"; "op_Modulus"
      "get_Zero"; "get_One"
      "DivideByInt";"get_Item"; "set_Item"
      "op_BitwiseAnd"; "op_BitwiseOr"; "op_ExclusiveOr"; "op_LeftShift"
      "op_RightShift"; "op_UnaryPlus"; "op_UnaryNegation"; "get_Sign"; "op_LogicalNot"
      "op_OnesComplement"; "Abs"; "Sqrt"; "Sin"; "Cos"; "Tan"
      "Sinh";  "Cosh"; "Tanh"; "Atan"; "Acos"; "Asin"; "Exp"; "Ceiling"; "Floor"; "Round"; "Log10"; "Log"; "Sqrt"
      "Truncate"; "op_Explicit"
      "Pow"; "Atan2" ]
    |> set
    
//-------------------------------------------------------------------------
// Run the constraint solver with undo (used during method overload resolution)

type Trace = 
    { mutable actions: ((unit -> unit) * (unit -> unit)) list }
    
    static member New () =  { actions = [] }

    member t.Undo () = List.iter (fun (_, a) -> a ()) t.actions
    member t.Push f undo = t.actions <- (f, undo) :: t.actions

type OptionalTrace = 
    | NoTrace
    | WithTrace of Trace

    member x.HasTrace = match x with NoTrace -> false | WithTrace _ -> true

    member t.Exec f undo = 
        match t with        
        | WithTrace trace -> trace.Push f undo; f()
        | NoTrace -> f()

    member t.AddFromReplay source =
        source.actions |> List.rev |>
            match t with        
            | WithTrace trace -> List.iter (fun (action, undo) -> trace.Push action undo; action())
            | NoTrace         -> List.iter (fun (action, _   ) -> action())

    member t.CollectThenUndoOrCommit predicate f =
        let newTrace = Trace.New()
        let res = f newTrace
        match predicate res, t with
        | false, _           -> newTrace.Undo()
        | true, WithTrace t -> t.actions <- newTrace.actions @ t.actions
        | true, NoTrace     -> ()
        res

let CollectThenUndo f = 
    let trace = Trace.New()
    let res = f trace
    trace.Undo()
    res

let FilterEachThenUndo f meths = 
    meths 
    |> List.choose (fun calledMeth -> 
        let trace = Trace.New()        
        let res = f trace calledMeth
        trace.Undo()
        match CheckNoErrorsAndGetWarnings res with 
        | None -> None 
        | Some (warns, res) -> Some (calledMeth, warns, trace, res))

let ShowAccessDomain ad =
    match ad with 
    | AccessibleFromEverywhere -> "public" 
    | AccessibleFrom _ -> "accessible"
    | AccessibleFromSomeFSharpCode -> "public, protected or internal" 
    | AccessibleFromSomewhere -> ""

//-------------------------------------------------------------------------
// Solve

exception NonRigidTypar of displayEnv: DisplayEnv * string option * range * TType * TType * range

/// Signal that there is still an unresolved overload in the constraint problem. The
/// unresolved overload constraint remains in the constraint state, and we skip any
/// further processing related to whichever overall adjustment to constraint solver state
/// is being processed.
///
// NOTE: The addition of this abort+skip appears to be a mistake which has crept into F# type inference,
// and its status is currently under review. See https://github.com/dotnet/fsharp/pull/8294 and others.
//
// Here is the history:
//    1. The local abort was added as part of an attempted performance optimization https://github.com/dotnet/fsharp/pull/1650
//       This change was released in the VS2017 GA release.
//
//    2. However, it also impacts the logic of type inference, by skipping checking.
//       Because of this an attempt was made to revert it in https://github.com/dotnet/fsharp/pull/4173.
//
//       Unfortunately, existing code had begun to depend on the new behaviours enabled by the
//       change, and the revert was abandoned before release in https://github.com/dotnet/fsharp/pull/4348
//
// Comments on soundness:
//    The use of the abort is normally sound because the SRTP constraint
//    will be subject to further processing at a later point.
//
//    However, it seems likely that the abort may result in other processing associated
//    with an overall constraint being skipped (e.g. the processing related to subsequent elements
//    of a tuple constraint).
exception AbortForFailedMemberConstraintResolution

/// This is used internally in method overload resolution
let IgnoreFailedMemberConstraintResolution f1 f2 =
    TryD 
        f1
        (function
         | AbortForFailedMemberConstraintResolution -> CompleteD
         | exn -> f2 exn)

/// This is used at (nearly all) entry points into the constraint solver to make sure that the
/// AbortForFailedMemberConstraintResolution error result is caught, the constraint recorded
/// as a post-inference check and processing continues.
///
/// Due to the legacy of the change https://github.com/dotnet/fsharp/pull/1650, some constraint
/// applications must be allowed to "succeed" with partial processing of the unification being
/// left in place, and no error being raised. This happens in cases where SRTP overload
/// resolution has failed. SRTP resolution is delayed and presumably resolved by later type information.
///
/// Quite a lot of code related to tasks has come to rely on this feature.
///
/// To ensure soundness, we double-check the constraint at the end of inference
/// with 'ErrorOnFailedMemberConstraintResolution' set to false.
let PostponeOnFailedMemberConstraintResolution (csenv: ConstraintSolverEnv) (trace: OptionalTrace) f1 f2 =
    TryD 
        (fun () ->
            let csenv = { csenv with ErrorOnFailedMemberConstraintResolution = true }
            f1 csenv)
        (function
         | AbortForFailedMemberConstraintResolution -> 
            // Postponed checking of constraints for failed SRTP resolutions is supported from F# 6.0 onwards
            // and is required for the "tasks" (aka ResumableStateMachines) feature.
            //
            // See https://github.com/dotnet/fsharp/issues/12188
            if csenv.g.langVersion.SupportsFeature LanguageFeature.ResumableStateMachines then
                trace.Exec
                    (fun () -> 
                        csenv.SolverState.PushPostInferenceCheck (preDefaults=true, check = fun () -> 
                            let csenv = { csenv with ErrorOnFailedMemberConstraintResolution = false }
                            f1 csenv |> RaiseOperationResult))
                    (fun () -> 
                        csenv.SolverState.PopPostInferenceCheck (preDefaults=true))
                
            CompleteD
         | exn -> f2 exn)

/// used to provide detail about non matched argument in overload resolution error message
exception ArgDoesNotMatchError of error: ErrorsFromAddingSubsumptionConstraint * calledMeth: CalledMeth<Expr> * calledArg: CalledArg * callerArg: CallerArg<Expr>

/// Represents a very local condition where we prefer to report errors before stripping type abbreviations.
exception LocallyAbortOperationThatLosesAbbrevs 

let localAbortD = ErrorD LocallyAbortOperationThatLosesAbbrevs

/// Return true if we would rather unify this variable v1 := v2 than vice versa
let PreferUnifyTypar (v1: Typar) (v2: Typar) =
    match v1.Rigidity, v2.Rigidity with 
    // Rigid > all
    | TyparRigidity.Rigid, _ -> false
    // Prefer to unify away WillBeRigid in favour of Rigid
    | TyparRigidity.WillBeRigid, TyparRigidity.Rigid -> true
    | TyparRigidity.WillBeRigid, TyparRigidity.WillBeRigid -> true
    | TyparRigidity.WillBeRigid, TyparRigidity.WarnIfNotRigid -> false
    | TyparRigidity.WillBeRigid, TyparRigidity.Anon -> false
    | TyparRigidity.WillBeRigid, TyparRigidity.Flexible -> false
    // Prefer to unify away WarnIfNotRigid in favour of Rigid
    | TyparRigidity.WarnIfNotRigid, TyparRigidity.Rigid -> true
    | TyparRigidity.WarnIfNotRigid, TyparRigidity.WillBeRigid -> true
    | TyparRigidity.WarnIfNotRigid, TyparRigidity.WarnIfNotRigid -> true
    | TyparRigidity.WarnIfNotRigid, TyparRigidity.Anon -> false
    | TyparRigidity.WarnIfNotRigid, TyparRigidity.Flexible -> false
    // Prefer to unify away anonymous variables in favour of Rigid, WarnIfNotRigid 
    | TyparRigidity.Anon, TyparRigidity.Rigid -> true
    | TyparRigidity.Anon, TyparRigidity.WillBeRigid -> true
    | TyparRigidity.Anon, TyparRigidity.WarnIfNotRigid -> true
    | TyparRigidity.Anon, TyparRigidity.Anon -> true
    | TyparRigidity.Anon, TyparRigidity.Flexible -> false
    // Prefer to unify away Flexible in favour of Rigid, WarnIfNotRigid or Anon
    | TyparRigidity.Flexible, TyparRigidity.Rigid -> true
    | TyparRigidity.Flexible, TyparRigidity.WillBeRigid -> true
    | TyparRigidity.Flexible, TyparRigidity.WarnIfNotRigid -> true
    | TyparRigidity.Flexible, TyparRigidity.Anon -> true
    | TyparRigidity.Flexible, TyparRigidity.Flexible -> 

      // Prefer to unify away compiler generated type vars
      match v1.IsCompilerGenerated, v2.IsCompilerGenerated with
      | true, false -> true
      | false, true -> false
      | _ -> 
         // Prefer to unify away non-error vars - gives better error recovery since we keep
         // error vars lying around, and can avoid giving errors about illegal polymorphism 
         // if they occur 
         match v1.IsFromError, v2.IsFromError with
         | true, false -> false
         | _ -> true

/// Reorder a list of (variable, exponent) pairs so that a variable that is Preferred
/// is at the head of the list, if possible
let FindPreferredTypar vs =
    let rec find vs = 
        match vs with
        | [] -> vs
        | (v: Typar, e) :: vs ->
            match find vs with
            | [] -> [(v, e)]
            | (v', e') :: vs' -> 
                if PreferUnifyTypar v v'
                then (v, e) :: vs
                else (v', e') :: (v, e) :: vs'
    find vs
  
let SubstMeasure (r: Typar) ms = 
    if r.Rigidity = TyparRigidity.Rigid then error(InternalError("SubstMeasure: rigid", r.Range))
    if r.Kind = TyparKind.Type then error(InternalError("SubstMeasure: kind=type", r.Range))

    match r.typar_solution with
    | None -> r.typar_solution <- Some (TType_measure ms)
    | Some _ -> error(InternalError("already solved", r.Range))

let rec TransactStaticReq (csenv: ConstraintSolverEnv) (trace: OptionalTrace) (tpr: Typar) req = 
    let m = csenv.m
    let g = csenv.g

    // Prior to feature InterfacesWithAbstractStaticMembers the StaticReq must match the
    // declared StaticReq. With feature InterfacesWithAbstractStaticMembers it is inferred
    // from the finalized constraints on the type variable.
    if not (g.langVersion.SupportsFeature LanguageFeature.InterfacesWithAbstractStaticMembers) && tpr.Rigidity.ErrorIfUnified && tpr.StaticReq <> req then
        ErrorD(ConstraintSolverError(FSComp.SR.csTypeCannotBeResolvedAtCompileTime(tpr.Name), m, m)) 
    else
        let orig = tpr.StaticReq
        trace.Exec (fun () -> tpr.SetStaticReq req) (fun () -> tpr.SetStaticReq orig)
        CompleteD

and SolveTypStaticReqTypar (csenv: ConstraintSolverEnv) trace req (tpr: Typar) =
    let orig = tpr.StaticReq
    let req2 = JoinTyparStaticReq req orig
    if orig <> req2 then TransactStaticReq csenv trace tpr req2 else CompleteD

and SolveTypStaticReq (csenv: ConstraintSolverEnv) trace req ty =
    match req with 
    | TyparStaticReq.None -> CompleteD
    | TyparStaticReq.HeadType -> 
        // requires that a type constructor be known at compile time 
        match stripTyparEqns ty with
        | TType_measure ms ->
            let vs = ListMeasureVarOccsWithNonZeroExponents ms
            trackErrors {
                for tpr, _ in vs do 
                    do! SolveTypStaticReqTypar csenv trace req tpr
            }
        | _ -> 
            match tryAnyParTy csenv.g ty with
            | ValueSome tpr -> SolveTypStaticReqTypar csenv trace req tpr
            | ValueNone -> CompleteD
      
let TransactDynamicReq (trace: OptionalTrace) (tpr: Typar) req = 
    let orig = tpr.DynamicReq
    trace.Exec (fun () -> tpr.SetDynamicReq req) (fun () -> tpr.SetDynamicReq orig)
    CompleteD

let SolveTypDynamicReq (csenv: ConstraintSolverEnv) trace req ty =
    match req with 
    | TyparDynamicReq.No -> CompleteD
    | TyparDynamicReq.Yes -> 
        match tryAnyParTy csenv.g ty with
        | ValueSome tpr when tpr.DynamicReq <> TyparDynamicReq.Yes ->
            TransactDynamicReq trace tpr TyparDynamicReq.Yes
        | _ -> CompleteD

let TransactIsCompatFlex (trace: OptionalTrace) (tpr: Typar) req = 
    let orig = tpr.IsCompatFlex
    trace.Exec (fun () -> tpr.SetIsCompatFlex req) (fun () -> tpr.SetIsCompatFlex orig)
    CompleteD

let SolveTypIsCompatFlex (csenv: ConstraintSolverEnv) trace req ty =
    if req then 
        match tryAnyParTy csenv.g ty with
        | ValueSome tpr when not tpr.IsCompatFlex -> TransactIsCompatFlex trace tpr req
        | _ -> CompleteD
    else
        CompleteD

let SubstMeasureWarnIfRigid (csenv: ConstraintSolverEnv) trace (v: Typar) ms =
    trackErrors {
        if v.Rigidity.WarnIfUnified && not (isAnyParTy csenv.g (TType_measure ms)) then
            // NOTE: we grab the name eagerly to make sure the type variable prints as a type variable 
            let tpnmOpt = if v.IsCompilerGenerated then None else Some v.Name
            do! SolveTypStaticReq csenv trace v.StaticReq (TType_measure ms)
            SubstMeasure v ms
            return! WarnD(NonRigidTypar(csenv.DisplayEnv, tpnmOpt, v.Range, TType_measure (Measure.Var v), TType_measure ms, csenv.m))
        else
            // Propagate static requirements from 'tp' to 'ty'
            do! SolveTypStaticReq csenv trace v.StaticReq (TType_measure ms)
            SubstMeasure v ms
            if v.Rigidity = TyparRigidity.Anon && measureEquiv csenv.g ms (Measure.One ms.Range) then 
                return! WarnD(Error(FSComp.SR.csCodeLessGeneric(), v.Range))
            else
                ()
    }

let IsRigid (csenv: ConstraintSolverEnv) (tp: Typar) =
    tp.Rigidity = TyparRigidity.Rigid
    || csenv.ExtraRigidTypars.Contains tp

/// Imperatively unify the unit-of-measure expression ms against 1.
/// There are three cases
/// - ms is (equivalent to) 1
/// - ms contains no non-rigid unit variables, and so cannot be unified with 1
/// - ms has the form v^e * ms' for some non-rigid variable v, non-zero exponent e, and measure expression ms'
///   the most general unifier is then simply v := ms' ^ -(1/e)
let UnifyMeasureWithOne (csenv: ConstraintSolverEnv) trace ms = 
    // Gather the rigid and non-rigid unit variables in this measure expression together with their exponents
    let rigidVars, nonRigidVars = 
        ListMeasureVarOccsWithNonZeroExponents ms
        |> List.partition (fun (v, _) -> IsRigid csenv v) 

    // If there is at least one non-rigid variable v with exponent e, then we can unify 
    match FindPreferredTypar nonRigidVars with
    | (v, e) :: vs ->
        let unexpandedCons = ListMeasureConOccsWithNonZeroExponents csenv.g false ms
        let newms = ProdMeasures (List.map (fun (c, e') -> Measure.RationalPower(Measure.Const(c, ms.Range), NegRational (DivRational e' e))) unexpandedCons 
                                @ List.map (fun (v, e') -> Measure.RationalPower (Measure.Var v, NegRational (DivRational e' e))) (vs @ rigidVars))

        SubstMeasureWarnIfRigid csenv trace v newms

    // Otherwise we require ms to be 1
    | [] -> if measureEquiv csenv.g ms (Measure.One ms.Range) then CompleteD else localAbortD
    
/// Imperatively unify unit-of-measure expression ms1 against ms2
let UnifyMeasures (csenv: ConstraintSolverEnv) trace ms1 ms2 = 
    UnifyMeasureWithOne csenv trace (Measure.Prod(ms1, Measure.Inv ms2, (unionRanges ms1.Range ms2.Range)))

/// Simplify a unit-of-measure expression ms that forms part of a type scheme. 
/// We make substitutions for vars, which are the (remaining) bound variables
///   in the scheme that we wish to simplify. 
let SimplifyMeasure g vars ms =
    let rec simp vars = 
        match FindPreferredTypar (List.filter (fun (_, e) -> SignRational e<>0) (List.map (fun v -> (v, MeasureVarExponent v ms)) vars)) with
        | [] -> 
          (vars, None)

        | (v, e) :: vs -> 
          let newvar = if v.IsCompilerGenerated then NewAnonTypar (TyparKind.Measure, v.Range, TyparRigidity.Flexible, v.StaticReq, v.DynamicReq)
                                                else NewNamedInferenceMeasureVar (v.Range, TyparRigidity.Flexible, v.StaticReq, v.Id)
          let remainingvars = ListSet.remove typarEq v vars
          let newvarExpr = if SignRational e < 0 then Measure.Inv (Measure.Var newvar) else Measure.Var newvar
          let nonZeroCon = ListMeasureConOccsWithNonZeroExponents g false ms
          let nonZeroVar = ListMeasureVarOccsWithNonZeroExponents ms
          let newms =
              ProdMeasures [
                  for (c, e') in nonZeroCon do
                      Measure.RationalPower (Measure.Const(c, ms.Range), NegRational (DivRational e' e)) 
                  for (v', e') in nonZeroVar do
                      if typarEq v v' then 
                          newvarExpr 
                      else 
                          Measure.RationalPower (Measure.Var v', NegRational (DivRational e' e))
              ]
          SubstMeasure v newms
          match vs with 
          | [] -> (remainingvars, Some newvar) 
          | _ -> simp (newvar :: remainingvars)
    simp vars

// Normalize a type ty that forms part of a unit-of-measure-polymorphic type scheme. 
//  Generalizable are the unit-of-measure variables that remain to be simplified. Generalized
// is a list of unit-of-measure variables that have already been generalized. 
let rec SimplifyMeasuresInType g resultFirst (generalizable, generalized as param) ty =
    match stripTyparEqns ty with 
    | TType_ucase(_, l)
    | TType_app (_, l, _) 
    | TType_anon (_,l)
    | TType_tuple (_, l) -> SimplifyMeasuresInTypes g param l

    | TType_fun (domainTy, rangeTy, _) ->
        if resultFirst then
            SimplifyMeasuresInTypes g param [rangeTy;domainTy]
        else
            SimplifyMeasuresInTypes g param [domainTy;rangeTy]        

    | TType_var _ -> param

    | TType_forall (_, tau) -> SimplifyMeasuresInType g resultFirst param tau

    | TType_measure unt -> 
        let generalizable', newlygeneralized = SimplifyMeasure g generalizable unt   
        match newlygeneralized with
        | None -> (generalizable', generalized)
        | Some v -> (generalizable', v :: generalized)

and SimplifyMeasuresInTypes g param tys = 
    match tys with
    | [] -> param
    | ty :: tys -> 
        let param' = SimplifyMeasuresInType g false param ty 
        SimplifyMeasuresInTypes g param' tys

let SimplifyMeasuresInConstraint g param c =
    match c with
    | TyparConstraint.DefaultsTo (_, ty, _) 
    | TyparConstraint.CoercesTo(ty, _) -> SimplifyMeasuresInType g false param ty
    | TyparConstraint.SimpleChoice (tys, _) -> SimplifyMeasuresInTypes g param tys
    | TyparConstraint.IsDelegate (ty1, ty2, _) -> SimplifyMeasuresInTypes g param [ty1;ty2]
    | _ -> param

let rec SimplifyMeasuresInConstraints g param cs = 
    match cs with
    | [] -> param
    | c :: cs ->
        let param' = SimplifyMeasuresInConstraint g param c
        SimplifyMeasuresInConstraints g param' cs

let rec GetMeasureVarGcdInType v ty =
    match stripTyparEqns ty with 
    | TType_ucase(_, l)
    | TType_app (_, l, _) 
    | TType_anon (_,l)
    | TType_tuple (_, l) -> GetMeasureVarGcdInTypes v l

    | TType_fun (domainTy, rangeTy, _) -> GcdRational (GetMeasureVarGcdInType v domainTy) (GetMeasureVarGcdInType v rangeTy)
    | TType_var _   -> ZeroRational
    | TType_forall (_, tau) -> GetMeasureVarGcdInType v tau
    | TType_measure unt -> MeasureVarExponent v unt

and GetMeasureVarGcdInTypes v tys =
    match tys with
    | [] -> ZeroRational
    | ty :: tys -> GcdRational (GetMeasureVarGcdInType v ty) (GetMeasureVarGcdInTypes v tys)
  
// Normalize the exponents on generalizable variables in a type
// by dividing them by their "rational gcd". For example, the type
// float<'u^(2/3)> -> float<'u^(4/3)> would be normalized to produce
// float<'u> -> float<'u^2> by dividing the exponents by 2/3.
let NormalizeExponentsInTypeScheme uvars ty =
  uvars |> List.map (fun v ->
    let expGcd = AbsRational (GetMeasureVarGcdInType v ty)
    if expGcd = OneRational || expGcd = ZeroRational then
        v 
    else
        let v' = NewAnonTypar (TyparKind.Measure, v.Range, TyparRigidity.Flexible, v.StaticReq, v.DynamicReq)
        SubstMeasure v (Measure.RationalPower (Measure.Var v', DivRational OneRational expGcd))
        v')
    
// We normalize unit-of-measure-polymorphic type schemes. There  
// are three reasons for doing this:
//   (1) to present concise and consistent type schemes to the programmer
//   (2) so that we can compute equivalence of type schemes in signature matching
//   (3) in order to produce a list of type parameters ordered as they appear in the (normalized) scheme.
//
// Representing the normal form as a matrix, with a row for each variable or base unit, 
// and a column for each unit-of-measure expression in the "skeleton" of the type. 
// Entries for generalizable variables are integers; other rows may contain non-integer exponents.
//  
// ( 0...0  a1  as1    b1  bs1    c1  cs1    ...)
// ( 0...0  0   0...0  b2  bs2    c2  cs2    ...)
// ( 0...0  0   0...0  0   0...0  c3  cs3    ...)
//...
// ( 0...0  0   0...0  0   0...0  0   0...0  ...)
//
// The normal form is unique; what's more, it can be used to force a variable ordering 
// because the first occurrence of a variable in a type is in a unit-of-measure expression with no 
// other "new" variables (a1, b2, c3, above). 
//
// The corner entries a1, b2, c3 are all positive. Entries lying above them (b1, c1, c2, etc) are
// non-negative and smaller than the corresponding corner entry. Entries as1, bs1, bs2, etc are arbitrary.
//
// Essentially this is the *reduced row echelon* matrix from linear algebra, with adjustment to ensure that
// exponents are integers where possible (in the reduced row echelon form, a1, b2, etc. would be 1, possibly
// forcing other entries to be non-integers).
let SimplifyMeasuresInTypeScheme g resultFirst (generalizable: Typar list) ty constraints =
    // Only bother if we're generalizing over at least one unit-of-measure variable 
    let uvars, vars = 
        generalizable
        |> List.partition (fun v -> v.Rigidity <> TyparRigidity.Rigid && v.Kind = TyparKind.Measure) 
 
    match uvars with
    | [] -> generalizable
    | _ :: _ ->
    let _, generalized = SimplifyMeasuresInType g resultFirst (SimplifyMeasuresInConstraints g (uvars, []) constraints) ty
    let generalized' = NormalizeExponentsInTypeScheme generalized ty 
    vars @ List.rev generalized'

let freshMeasure () = Measure.Var (NewInferenceMeasurePar ())

let CheckWarnIfRigid (csenv: ConstraintSolverEnv) ty1 (r: Typar) ty =
    let g = csenv.g
    let denv = csenv.DisplayEnv
    if not r.Rigidity.WarnIfUnified then CompleteD else
    let needsWarning =
        match tryAnyParTy g ty with
        | ValueNone -> true
        | ValueSome tp2 ->
            not tp2.IsCompilerGenerated &&
                (r.IsCompilerGenerated ||
                 // exclude this warning for two identically named user-specified type parameters, e.g. from different mutually recursive functions or types
                 r.DisplayName <> tp2.DisplayName)

    if needsWarning then
        // NOTE: we grab the name eagerly to make sure the type variable prints as a type variable 
        let tpnmOpt = if r.IsCompilerGenerated then None else Some r.Name 
        WarnD(NonRigidTypar(denv, tpnmOpt, r.Range, ty1, ty, csenv.m)) 
    else 
        CompleteD

/// Add the constraint "ty1 = ty" to the constraint problem, where ty1 is a type variable. 
/// Propagate all effects of adding this constraint, e.g. to solve other variables 
let rec SolveTyparEqualsTypePart1 (csenv: ConstraintSolverEnv) m2 (trace: OptionalTrace) ty1 r ty =
    trackErrors {
        // The types may still be equivalent due to abbreviations, which we are trying not to eliminate 
        if typeEquiv csenv.g ty1 ty then () else
        // The famous 'occursCheck' check to catch "infinite types" like 'a = list<'a> - see also https://github.com/dotnet/fsharp/issues/1170
        if occursCheck csenv.g r ty then return! ErrorD (ConstraintSolverInfiniteTypes(csenv.DisplayEnv, csenv.eContextInfo, ty1, ty, csenv.m, m2)) else
        // Note: warn _and_ continue! 
        do! CheckWarnIfRigid csenv ty1 r ty
        // Record the solution before we solve the constraints, since 
        // We may need to make use of the equation when solving the constraints. 
        // Record a entry in the undo trace if one is provided 
        trace.Exec (fun () -> r.typar_solution <- Some ty) (fun () -> r.typar_solution <- None)
    }  

and SolveTyparEqualsTypePart2 (csenv: ConstraintSolverEnv) ndeep m2 (trace: OptionalTrace) (r: Typar) ty =
    trackErrors {
        // Only solve constraints if this is not an error var 
        if r.IsFromError then () else

        // Check to see if this type variable is relevant to any trait constraints. 
        // If so, re-solve the relevant constraints. 
        if csenv.SolverState.ExtraCxs.ContainsKey r.Stamp then 
            do! RepeatWhileD ndeep (fun ndeep -> SolveRelevantMemberConstraintsForTypar csenv ndeep PermitWeakResolution.No trace r)

        // Re-solve the other constraints associated with this type variable 
        return! SolveTypMeetsTyparConstraints csenv ndeep m2 trace ty r

    }

/// Apply the constraints on 'typar' to the type 'ty'
and SolveTypMeetsTyparConstraints (csenv: ConstraintSolverEnv) ndeep m2 trace ty (r: Typar) =
    trackErrors {
        let g = csenv.g

        // Propagate compat flex requirements from 'tp' to 'ty'
        do! SolveTypIsCompatFlex csenv trace r.IsCompatFlex ty

            // Propagate dynamic requirements from 'tp' to 'ty'
        do! SolveTypDynamicReq csenv trace r.DynamicReq ty

        // Propagate static requirements from 'tp' to 'ty' 
        do! SolveTypStaticReq csenv trace r.StaticReq ty

        if not (g.langVersion.SupportsFeature LanguageFeature.InterfacesWithAbstractStaticMembers) then
            // Propagate static requirements from 'tp' to 'ty'
            //
            // If IWSAMs are not supported then this is done on a per-type-variable basis when constraints
            // are applied - see other calls to SolveTypStaticReq
            do! SolveTypStaticReq csenv trace r.StaticReq ty

        // Solve constraints on 'tp' w.r.t. 'ty' 
        for e in r.Constraints do
        do!
        match e with
        | TyparConstraint.DefaultsTo (priority, dty, m) -> 
            if typeEquiv g ty dty then 
                CompleteD
            else
                match tryDestTyparTy g ty with
                | ValueNone -> CompleteD
                | ValueSome destTypar ->
                    AddConstraint csenv ndeep m2 trace destTypar (TyparConstraint.DefaultsTo(priority, dty, m))
            
        | TyparConstraint.NotSupportsNull m2             -> SolveTypeUseNotSupportsNull         csenv ndeep m2 trace ty
        | TyparConstraint.SupportsNull m2                -> SolveTypeUseSupportsNull            csenv ndeep m2 trace ty
        | TyparConstraint.IsEnum(underlyingTy, m2)       -> SolveTypeIsEnum                     csenv ndeep m2 trace ty underlyingTy
        | TyparConstraint.SupportsComparison(m2)         -> SolveTypeSupportsComparison         csenv ndeep m2 trace ty
        | TyparConstraint.SupportsEquality(m2)           -> SolveTypeSupportsEquality           csenv ndeep m2 trace ty
        | TyparConstraint.IsDelegate(aty, bty, m2)       -> SolveTypeIsDelegate                 csenv ndeep m2 trace ty aty bty
        | TyparConstraint.IsNonNullableStruct m2         -> SolveTypeIsNonNullableValueType     csenv ndeep m2 trace ty
        | TyparConstraint.IsUnmanaged m2                 -> SolveTypeIsUnmanaged                csenv ndeep m2 trace ty
        | TyparConstraint.AllowsRefStruct _              -> CompleteD
        | TyparConstraint.IsReferenceType m2             -> SolveTypeIsReferenceType            csenv ndeep m2 trace ty
        | TyparConstraint.RequiresDefaultConstructor m2  -> SolveTypeRequiresDefaultConstructor csenv ndeep m2 trace ty
        | TyparConstraint.SimpleChoice(tys, m2)          -> SolveTypeChoice                     csenv ndeep m2 trace ty tys
        | TyparConstraint.CoercesTo(ty2, m2)             -> SolveTypeSubsumesTypeKeepAbbrevs    csenv ndeep m2 trace None ty2 ty
        | TyparConstraint.MayResolveMember(traitInfo, m2) -> 
            SolveMemberConstraint csenv false PermitWeakResolution.No ndeep m2 trace traitInfo |> OperationResult.ignore
    }

and shouldWarnUselessNullCheck (csenv:ConstraintSolverEnv) =
    csenv.g.checkNullness &&
    csenv.SolverState.WarnWhenUsingWithoutNullOnAWithNullTarget.IsSome    

// nullness1: actual
// nullness2: expected
and SolveNullnessEquiv (csenv: ConstraintSolverEnv) m2 (trace: OptionalTrace) ty1 ty2 nullness1 nullness2 =
    match nullness1, nullness2 with
    | Nullness.Variable nv1, Nullness.Variable nv2 when nv1 === nv2 -> 
        CompleteD
    | Nullness.Variable nv1, _ when nv1.IsSolved ->
        SolveNullnessEquiv csenv m2 trace ty1 ty2 nv1.Solution nullness2
    | _, Nullness.Variable nv2 when nv2.IsSolved -> 
        SolveNullnessEquiv csenv m2 trace ty1 ty2 nullness1 nv2.Solution
    | Nullness.Variable nv1, _ ->
        trace.Exec (fun () -> nv1.Set nullness2) (fun () -> nv1.Unset())
        CompleteD
    | _, Nullness.Variable nv2 -> 
        trace.Exec (fun () -> nv2.Set nullness1) (fun () -> nv2.Unset())
        CompleteD
    | Nullness.Known n1, Nullness.Known n2 -> 
        match n1, n2 with 
        | NullnessInfo.AmbivalentToNull, _ -> CompleteD
        | _, NullnessInfo.AmbivalentToNull -> CompleteD
        | NullnessInfo.WithNull, NullnessInfo.WithNull -> CompleteD
        | NullnessInfo.WithoutNull, NullnessInfo.WithoutNull -> CompleteD
        // Warn for 'strict "must pass null"` APIs like Option.ofObj
        | NullnessInfo.WithNull, NullnessInfo.WithoutNull when shouldWarnUselessNullCheck csenv -> 
            WarnD(Error(FSComp.SR.tcPassingWithoutNullToANullableExpectingFunc (csenv.SolverState.WarnWhenUsingWithoutNullOnAWithNullTarget.Value),m2))    
        // Allow expected of WithNull and actual of WithoutNull except for specially marked APIs (handled above)        
        | NullnessInfo.WithNull, NullnessInfo.WithoutNull -> CompleteD
        | _ -> 
            if csenv.g.checkNullness then 
                WarnD(ConstraintSolverNullnessWarningEquivWithTypes(csenv.DisplayEnv, ty1, ty2, n1, n2, csenv.m, m2))
            else
                CompleteD
        
// nullness1: target
// nullness2: source
and SolveNullnessSubsumesNullness (csenv: ConstraintSolverEnv) m2 (trace: OptionalTrace) ty1 ty2 nullness1 nullness2 =
    match nullness1, nullness2 with
    | Nullness.Variable nv1, Nullness.Variable nv2 when nv1 === nv2 -> 
        CompleteD
    | Nullness.Variable nv1, _ when nv1.IsSolved -> 
        SolveNullnessSubsumesNullness csenv m2 trace ty1 ty2 nv1.Solution nullness2
    | _, Nullness.Variable nv2 when nv2.IsSolved -> 
        SolveNullnessSubsumesNullness csenv m2 trace ty1 ty2 nullness1 nv2.Solution
    | Nullness.Variable _nv1, Nullness.Known NullnessInfo.WithoutNull  -> 
        CompleteD
    | Nullness.Variable nv1, _ -> 
        trace.Exec (fun () ->   nv1.Set nullness2) (fun () -> nv1.Unset())
        CompleteD
    | _, Nullness.Variable nv2 -> 
        trace.Exec (fun () -> nv2.Set nullness1) (fun () -> nv2.Unset())
        CompleteD
    | Nullness.Known n1, Nullness.Known n2 -> 
        match n1, n2 with 
        | NullnessInfo.AmbivalentToNull, _ -> CompleteD
        | _, NullnessInfo.AmbivalentToNull -> CompleteD
        | NullnessInfo.WithNull, NullnessInfo.WithNull -> CompleteD
        | NullnessInfo.WithoutNull, NullnessInfo.WithoutNull -> CompleteD
        // Warn for 'strict "must pass null"` APIs like Option.ofObj
        | NullnessInfo.WithNull, NullnessInfo.WithoutNull when shouldWarnUselessNullCheck csenv -> 
            WarnD(Error(FSComp.SR.tcPassingWithoutNullToANullableExpectingFunc (csenv.SolverState.WarnWhenUsingWithoutNullOnAWithNullTarget.Value),m2))
        // Allow target of WithNull and actual of WithoutNull
        | NullnessInfo.WithNull, NullnessInfo.WithoutNull ->             
            CompleteD
        | NullnessInfo.WithoutNull, NullnessInfo.WithNull -> 
            if csenv.g.checkNullness then               
                 WarnD(ConstraintSolverNullnessWarningWithTypes(csenv.DisplayEnv, ty1, ty2, n1, n2, csenv.m, m2)) 
            else
                CompleteD
        
and SolveTyparEqualsType (csenv: ConstraintSolverEnv) ndeep m2 (trace: OptionalTrace) ty1 ty =
    trackErrors {
        let m = csenv.m
        do! DepthCheck ndeep m
        match ty1 with 
        | TType_var (r, _)
        | TType_measure (Measure.Var r) ->
            do! SolveTyparEqualsTypePart1 csenv m2 trace ty1 r ty 
            do! SolveTyparEqualsTypePart2 csenv ndeep m2 trace r ty 
        | _ -> failwith "SolveTyparEqualsType"
    }

// Like SolveTyparEqualsType but asserts all typar equalities simultaneously instead of one by one
and SolveTyparsEqualTypes (csenv: ConstraintSolverEnv) ndeep m2 (trace: OptionalTrace) tpTys tys =
    trackErrors {
        do! Iterate2D (
                fun tpTy ty ->
                    match tpTy with
                    | TType_var (r, _)
                    | TType_measure (Measure.Var r) ->
                        SolveTyparEqualsTypePart1 csenv m2 trace tpTy r ty
                    | _ ->
                        failwith "SolveTyparsEqualTypes") tpTys tys
        do! Iterate2D (
                fun tpTy ty -> 
                    match tpTy with 
                    | TType_var (r, _)
                    | TType_measure (Measure.Var r) ->
                        SolveTyparEqualsTypePart2 csenv ndeep m2 trace r ty 
                    | _ ->
                        failwith "SolveTyparsEqualTypes") tpTys tys
    }

and SolveAnonInfoEqualsAnonInfo (csenv: ConstraintSolverEnv) m2 (anonInfo1: AnonRecdTypeInfo) (anonInfo2: AnonRecdTypeInfo) = 
    if evalTupInfoIsStruct anonInfo1.TupInfo <> evalTupInfoIsStruct anonInfo2.TupInfo then
        ErrorD (ConstraintSolverError(FSComp.SR.tcTupleStructMismatch(), csenv.m,m2))
    else
        trackErrors {
            if not (ccuEq anonInfo1.Assembly anonInfo2.Assembly) then
                do! ErrorD (ConstraintSolverError(FSComp.SR.tcAnonRecdCcuMismatch(anonInfo1.Assembly.AssemblyName, anonInfo2.Assembly.AssemblyName), csenv.m,m2))
                
            if anonInfo1.SortedNames <> anonInfo2.SortedNames then 
                let (|Subset|Superset|Overlap|CompletelyDifferent|) (first, second) =
                    let first = Set first
                    let second = Set second
                    let secondOnly = Set.toList (second - first)
                    let firstOnly = Set.toList (first - second)

                    if second.IsSubsetOf first then
                        Subset firstOnly
                    elif second.IsSupersetOf first then
                        Superset secondOnly
                    elif Set.intersect first second <> Set.empty then
                        Overlap(firstOnly, secondOnly)
                    else
                        let first = Set.toList first
                        let second = Set.toList second
                        CompletelyDifferent(first, second)
                
                let message =
                    match anonInfo1.SortedNames, anonInfo2.SortedNames with
                    | Subset missingFields ->
                        match missingFields with
                        | [missingField] ->
                            FSComp.SR.tcAnonRecdSingleFieldNameSubset(string missingField)
                        | _ ->
                            let missingFields = missingFields |> List.map(sprintf "'%s'")
                            let missingFields = String.concat ", " missingFields
                            FSComp.SR.tcAnonRecdMultipleFieldsNameSubset(string missingFields)
                    | Superset extraFields ->
                        match extraFields with
                        | [extraField] ->
                            FSComp.SR.tcAnonRecdSingleFieldNameSuperset(string extraField)
                        | _ ->
                            let extraFields = extraFields |> List.map(sprintf "'%s'")
                            let extraFields = String.concat ", " extraFields
                            FSComp.SR.tcAnonRecdMultipleFieldsNameSuperset(string extraFields)
                    | Overlap (missingFields, extraFields) ->
                        FSComp.SR.tcAnonRecdFieldNameMismatch(string missingFields, string extraFields)
                    | CompletelyDifferent missingFields ->
                        let missingFields, usedFields = missingFields
                        match missingFields, usedFields with
                        | [ missingField ], [ usedField ] ->
                            FSComp.SR.tcAnonRecdSingleFieldNameSingleDifferent(missingField, usedField)
                        | [ missingField ], usedFields ->
                            let usedFields = usedFields |> List.map(sprintf "'%s'")
                            let usedFields = String.concat ", " usedFields
                            FSComp.SR.tcAnonRecdSingleFieldNameMultipleDifferent(missingField, usedFields)
                        | missingFields, [ usedField ] ->
                            let missingFields = missingFields |> List.map(sprintf "'%s'")
                            let missingFields = String.concat ", " missingFields
                            FSComp.SR.tcAnonRecdMultipleFieldNameSingleDifferent(missingFields, usedField)
                        
                        | missingFields, usedFields ->
                            let missingFields = missingFields |> List.map(sprintf "'%s'")
                            let missingFields = String.concat ", " missingFields
                            let usedFields = usedFields |> List.map(sprintf "'%s'")
                            let usedFields = String.concat ", " usedFields
                            FSComp.SR.tcAnonRecdMultipleFieldNameMultipleDifferent(missingFields, usedFields)
                
                do! ErrorD (ConstraintSolverError(message, csenv.m,m2)) 
            else 
                do! ResultD()
        }

/// Add the constraint "ty1 = ty2" to the constraint problem. 
/// Propagate all effects of adding this constraint, e.g. to solve type variables 
and SolveTypeEqualsType (csenv: ConstraintSolverEnv) ndeep m2 (trace: OptionalTrace) (cxsln:(TraitConstraintInfo * TraitConstraintSln) option) ty1 ty2 = 
    let ndeep = ndeep + 1
    let aenv = csenv.EquivEnv
    let g = csenv.g

    match cxsln with
    | Some (traitInfo, traitSln) when traitInfo.Solution.IsNone -> 
        // If this is an overload resolution at this point it's safe to assume the candidate member being evaluated solves this member constraint.
        TransactMemberConstraintSolution traitInfo trace traitSln
    | _ -> ()

    if ty1 === ty2 then
        CompleteD 
    else
        let canShortcut = not trace.HasTrace
        let sty1 = stripTyEqnsA csenv.g canShortcut ty1
        let sty2 = stripTyEqnsA csenv.g canShortcut ty2

        let csenv = 
            match ty1 with 
            | TType.TType_var(r,_) when r.typar_flags.IsSupportsNullFlex -> 
                { csenv with IsSupportsNullFlex = true}
            | _ -> csenv

        match sty1, sty2 with
        // type vars inside forall-types may be alpha-equivalent 
        | TType_var (tp1, nullness1), TType_var (tp2, nullness2) when typarEq tp1 tp2 || (match aenv.EquivTypars.TryFind tp1 with | Some tpTy1 when typeEquiv g tpTy1 ty2 -> true | _ -> false) ->
            SolveNullnessEquiv csenv m2 trace ty1 ty2 nullness1 nullness2

        | TType_var (tp1, nullness1), TType_var (tp2, nullness2) when PreferUnifyTypar tp1 tp2 -> 
            match nullness1.TryEvaluate(), nullness2.TryEvaluate() with
            // Unifying 'T1? and 'T2?
            | ValueSome NullnessInfo.WithNull, ValueSome NullnessInfo.WithNull ->
                SolveTyparEqualsType csenv ndeep m2 trace sty1 (TType_var (tp2, g.knownWithoutNull)) 
            | ValueSome NullnessInfo.WithNull, ValueSome NullnessInfo.WithoutNull ->
                let tpNew = NewCompGenTypar(TyparKind.Type, TyparRigidity.Flexible, TyparStaticReq.None, TyparDynamicReq.No, false)
                trackErrors {
                    do! SolveTypeEqualsType csenv ndeep m2 trace cxsln sty2 (TType_var(tpNew, g.knownWithNull))
                    do! SolveTypeEqualsType csenv ndeep m2 trace cxsln (TType_var(tpNew, g.knownWithoutNull)) sty1
                }
            //// Unifying 'T1 % and 'T2 %
            //| ValueSome NullnessInfo.AmbivalentToNull, ValueSome NullnessInfo.AmbivalentToNull ->
            //    SolveTyparEqualsType csenv ndeep m2 trace sty1 (TType_var (tp2, g.knownWithoutNull)) 
            | _ ->
                trackErrors {
                    do! SolveTyparEqualsType csenv ndeep m2 trace sty1 ty2
                    let nullnessAfterSolution1 = combineNullness (nullnessOfTy g sty1) nullness1
                    do! SolveNullnessEquiv csenv m2 trace ty1 ty2 nullnessAfterSolution1 nullness2
                }


        | TType_var (tp1, nullness1), TType_var (tp2, nullness2) when not csenv.MatchingOnly && PreferUnifyTypar tp2 tp1 ->
            match nullness1.TryEvaluate(), nullness2.TryEvaluate() with
            // Unifying 'T1? and 'T2?
            | ValueSome NullnessInfo.WithNull, ValueSome NullnessInfo.WithNull ->
                SolveTyparEqualsType csenv ndeep m2 trace sty2 (TType_var (tp1, g.knownWithoutNull)) 
            | ValueSome NullnessInfo.WithNull, ValueSome NullnessInfo.WithoutNull ->
                let tpNew = NewCompGenTypar(TyparKind.Type, TyparRigidity.Flexible, TyparStaticReq.None, TyparDynamicReq.No, false)
                trackErrors {
                    do! SolveTypeEqualsType csenv ndeep m2 trace cxsln sty2 (TType_var(tpNew, g.knownWithNull))
                    do! SolveTypeEqualsType csenv ndeep m2 trace cxsln (TType_var(tpNew, g.knownWithoutNull)) sty1      
                }           
            //// Unifying 'T1 % and 'T2 % 
            //| ValueSome NullnessInfo.AmbivalentToNull, ValueSome NullnessInfo.AmbivalentToNull ->
            //    SolveTyparEqualsType csenv ndeep m2 trace sty2 (TType_var (tp1, g.knownWithoutNull)) 
            | _ ->
                // Unifying 'T1 ? and 'T2 % 
                // Unifying 'T1 % and 'T2 ?
                trackErrors {
                    do! SolveTyparEqualsType csenv ndeep m2 trace sty2 ty1
                    let nullnessAfterSolution2 = combineNullness (nullnessOfTy g sty2) nullness2
                    do! SolveNullnessEquiv csenv m2 trace ty1 ty2 nullness1 nullnessAfterSolution2
                }

        | TType_var (tp1, nullness1), _ when not (IsRigid csenv tp1) ->
            match nullness1.TryEvaluate(), (nullnessOfTy g sty2).TryEvaluate() with
            // Unifying 'T1? and 'T2? 
            | ValueSome NullnessInfo.WithNull, ValueSome NullnessInfo.WithNull ->
                SolveTyparEqualsType csenv ndeep m2 trace sty1 (replaceNullnessOfTy g.knownWithoutNull sty2)
            | ValueSome NullnessInfo.WithoutNull, ValueSome NullnessInfo.WithoutNull when 
                csenv.IsSupportsNullFlex && 
                isAppTy g sty2 &&
                tp1 |> HasConstraint _.IsSupportsNull  &&
                not(tp1 |> HasConstraint _.IsIsNonNullableStruct)->
                    let tpNew = NewCompGenTypar(TyparKind.Type, TyparRigidity.Flexible, TyparStaticReq.None, TyparDynamicReq.No, false)               
                    trackErrors {                    
                        do! SolveTypeEqualsType csenv ndeep m2 trace cxsln (TType_var(tpNew, g.knownWithoutNull)) sty2
                        do! SolveTypeEqualsType csenv ndeep m2 trace cxsln ty1 (TType_var(tpNew, g.knownWithNull))
                    }
            // Unifying 'T1 % and 'T2 % 
            //| ValueSome NullnessInfo.AmbivalentToNull, ValueSome NullnessInfo.AmbivalentToNull ->
            //    SolveTyparEqualsType csenv ndeep m2 trace sty1 (replaceNullnessOfTy g.knownWithoutNull sty2) 
            | _ ->
                trackErrors {
                    do! SolveTyparEqualsType csenv ndeep m2 trace sty1 ty2
                    let nullnessAfterSolution1 = combineNullness (nullnessOfTy g sty1) nullness1
                    do! SolveNullnessEquiv csenv m2 trace ty1 ty2 nullnessAfterSolution1 (nullnessOfTy g sty2)
                }

        | _, TType_var (tp2, nullness2) when not csenv.MatchingOnly && not (IsRigid csenv tp2) ->
            match (nullnessOfTy g sty1).TryEvaluate(), nullness2.TryEvaluate() with
            // Unifying 'T1? and 'T2? 
            | ValueSome NullnessInfo.WithNull, ValueSome NullnessInfo.WithNull ->
                SolveTyparEqualsType csenv ndeep m2 trace sty2 (replaceNullnessOfTy g.knownWithoutNull sty1)
            // Unifying 'T1 % and 'T2 % 
            //| ValueSome NullnessInfo.AmbivalentToNull, ValueSome NullnessInfo.AmbivalentToNull ->
            //    SolveTyparEqualsType csenv ndeep m2 trace sty2 (replaceNullnessOfTy g.knownWithoutNull sty1)
            | _ ->
                trackErrors {
                    do! SolveTyparEqualsType csenv ndeep m2 trace sty2 ty1
                    let nullnessAfterSolution2 = combineNullness (nullnessOfTy g sty2) nullness2
                    do! SolveNullnessEquiv csenv m2 trace ty1 ty2 (nullnessOfTy g sty1) nullnessAfterSolution2
                }

        // Catch float<_>=float<1>, float32<_>=float32<1> and decimal<_>=decimal<1> 
        | (_, TType_app (tc2, [ms2], _)) when (tc2.IsMeasureableReprTycon && typeEquiv csenv.g sty1 (reduceTyconRefMeasureableOrProvided csenv.g tc2 [ms2])) ->
            trackErrors {
                do! SolveTypeEqualsType csenv ndeep m2 trace None (TType_measure(Measure.One m2)) ms2
                do! SolveNullnessEquiv csenv m2 trace ty1 ty2 (nullnessOfTy g sty1) (nullnessOfTy g sty2)
            }

        | (TType_app (tc1, [ms1], _), _) when (tc1.IsMeasureableReprTycon && typeEquiv csenv.g sty2 (reduceTyconRefMeasureableOrProvided csenv.g tc1 [ms1])) ->
            trackErrors {
                do! SolveTypeEqualsType csenv ndeep m2 trace None ms1 (TType_measure(Measure.One m2))
                do! SolveNullnessEquiv csenv m2 trace ty1 ty2 (nullnessOfTy g sty1) (nullnessOfTy g sty2)
            }

        | TType_app (tc1, l1, _), TType_app (tc2, l2, _) when tyconRefEq g tc1 tc2  ->
            trackErrors {
                do! SolveTypeEqualsTypeEqns csenv ndeep m2 trace None l1 l2
                do! SolveNullnessEquiv csenv m2 trace ty1 ty2 (nullnessOfTy g sty1) (nullnessOfTy g sty2)
            }
        | TType_app _, TType_app _ ->
            localAbortD

        | TType_tuple (tupInfo1, l1), TType_tuple (tupInfo2, l2) ->
            if evalTupInfoIsStruct tupInfo1 <> evalTupInfoIsStruct tupInfo2 then
                ErrorD (ConstraintSolverError(FSComp.SR.tcTupleStructMismatch(), csenv.m, m2))
            else
                SolveTypeEqualsTypeEqns csenv ndeep m2 trace None l1 l2

        | TType_anon (anonInfo1, l1),TType_anon (anonInfo2, l2) -> 
            trackErrors {
                do! SolveAnonInfoEqualsAnonInfo csenv m2 anonInfo1 anonInfo2
                do! SolveTypeEqualsTypeEqns csenv ndeep m2 trace None l1 l2
            }

        | TType_fun (domainTy1, rangeTy1, nullness1), TType_fun (domainTy2, rangeTy2, nullness2) ->
            trackErrors {
                do! SolveFunTypeEqn csenv ndeep m2 trace None domainTy1 domainTy2 rangeTy1 rangeTy2
                do! SolveNullnessEquiv csenv m2 trace ty1 ty2 nullness1 nullness2
            }

        | TType_measure ms1, TType_measure ms2 ->
            UnifyMeasures csenv trace ms1 ms2

        | TType_forall(tps1, bodyTy1), TType_forall(tps2, bodyTy2) ->
            if tps1.Length <> tps2.Length then
                localAbortD
            else
                let aenv = aenv.BindEquivTypars tps1 tps2
                let csenv = {csenv with EquivEnv = aenv }
                if not (typarsAEquiv g aenv tps1 tps2) then
                    localAbortD
                else
                    SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace bodyTy1 bodyTy2

        | TType_ucase (uc1, l1), TType_ucase (uc2, l2) when g.unionCaseRefEq uc1 uc2 ->
            SolveTypeEqualsTypeEqns csenv ndeep m2 trace None l1 l2

        | _  -> localAbortD

and SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace ty1 ty2 =
    SolveTypeEqualsTypeKeepAbbrevsWithCxsln csenv ndeep m2 trace None ty1 ty2

and private SolveTypeEqualsTypeKeepAbbrevsWithCxsln csenv ndeep m2 trace cxsln ty1 ty2 = 
   // Back out of expansions of type abbreviations to give improved error messages. 
   // Note: any "normalization" of equations on type variables must respect the trace parameter
   TryD (fun () -> SolveTypeEqualsType csenv ndeep m2 trace cxsln ty1 ty2)
        (function
        | LocallyAbortOperationThatLosesAbbrevs -> ErrorD(ConstraintSolverTypesNotInEqualityRelation(csenv.DisplayEnv, ty1, ty2, csenv.m, m2, csenv.eContextInfo))
        | err -> ErrorD err)

and SolveTypeEqualsTypeEqns csenv ndeep m2 trace cxsln origl1 origl2 =
   match origl1, origl2 with
   | [], [] -> CompleteD
   | _ ->
       // We unwind Iterate2D by hand here for performance reasons.
       let rec loop l1 l2 =
           match l1, l2 with
           | [], [] -> CompleteD
           | h1 :: t1, h2 :: t2 when t1.Length = t2.Length ->
               trackErrors {
                   do! SolveTypeEqualsTypeKeepAbbrevsWithCxsln csenv ndeep m2 trace cxsln h1 h2
                   do! loop t1 t2
               }
           | _ ->
               ErrorD(ConstraintSolverTupleDiffLengths(csenv.DisplayEnv, csenv.eContextInfo, origl1, origl2, csenv.m, m2)) 
       loop origl1 origl2

and SolveTypeEqualsTypeWithContravarianceEqns (csenv:ConstraintSolverEnv) ndeep m2 trace cxsln origl1 origl2 typars tyconRef =
   let isContravariant (t:Typar) = 
        t.typar_opt_data 
        |> Option.map (fun d -> d.typar_is_contravariant) 
        |> Option.defaultValue(false)
   
   // Special case for IEquatable<T>: treat its type parameter as contravariant for nullness purposes
   // This matches C# behavior where IEquatable<T> is treated as contravariant for nullness, even though
   // it's not formally marked as contravariant in IL.
   // See: https://github.com/dotnet/fsharp/issues/18759 and https://github.com/dotnet/roslyn/issues/37187
   let isIEquatableContravariantForNullness = tyconRefEq csenv.g tyconRef csenv.g.system_GenericIEquatable_tcref

   match origl1, origl2, typars with
   | [], [], [] -> CompleteD
   | _ ->
       // We unwind Iterate2D by hand here for performance reasons.
       let rec loop l1 l2 tps =
           match l1, l2, tps with
           | [], [], [] -> CompleteD
           | h1 :: t1, h2 :: t2, hTp :: tTps when t1.Length = t2.Length && t1.Length = tTps.Length ->
               trackErrors {
                    let h1 =
                        // For contravariant typars (`<in T> in C#'), if the required type is WithNull, the actual type can have any nullness it wants
                        // Without this added logic, their nullness would be forced to be equal.
                        // Special case: IEquatable<T> is treated as contravariant for nullness purposes to match C# behavior
                        if (isContravariant hTp || isIEquatableContravariantForNullness) && (nullnessOfTy csenv.g h2).TryEvaluate() = ValueSome NullnessInfo.WithNull  then                            
                            replaceNullnessOfTy csenv.g.knownWithNull h1
                        else
                            h1
                    
                    do! SolveTypeEqualsTypeKeepAbbrevsWithCxsln csenv ndeep m2 trace cxsln h1 h2
                    do! loop t1 t2 tTps
               }
           | _ ->
               ErrorD(ConstraintSolverTupleDiffLengths(csenv.DisplayEnv, csenv.eContextInfo, origl1, origl2, csenv.m, m2)) 
       loop origl1 origl2 typars

and SolveFunTypeEqn csenv ndeep m2 trace cxsln domainTy1 domainTy2 rangeTy1 rangeTy2 =
    trackErrors {
        let g = csenv.g
        let domainTy2 = reqTyForArgumentNullnessInference g domainTy1 domainTy2
        do! SolveTypeEqualsTypeKeepAbbrevsWithCxsln csenv ndeep m2 trace cxsln domainTy2 domainTy1
        return! SolveTypeEqualsTypeKeepAbbrevsWithCxsln csenv ndeep m2 trace cxsln rangeTy1 rangeTy2
    }

// ty1: expected
// ty2: actual
//
// "ty2 casts to ty1"
// "a value of type ty2 can be used where a value of type ty1 is expected"
and SolveTypeSubsumesType (csenv: ConstraintSolverEnv) ndeep m2 (trace: OptionalTrace) cxsln ty1 ty2 =     
    let ndeep = ndeep + 1
    let g = csenv.g
    let canShortcut = not trace.HasTrace

    // 'a :> objnull ---> <solved> 
    if isObjNullTy g ty1 then 
        CompleteD
    elif isObjTyAnyNullness g ty1 && not csenv.MatchingOnly && not(isTyparTy g ty2) then
        let nullness t = t |> stripTyEqnsA g canShortcut |> nullnessOfTy g
        SolveNullnessSubsumesNullness csenv m2 trace ty1 ty2 (nullness ty1) (nullness ty2)
    else         
        let sty1 = stripTyEqnsA csenv.g canShortcut ty1
        let sty2 = stripTyEqnsA csenv.g canShortcut ty2
        let amap = csenv.amap
        let aenv = csenv.EquivEnv
        let denv = csenv.DisplayEnv

        match sty1, sty2 with 
        | TType_var (tp1, nullness1) , _ ->
            match aenv.EquivTypars.TryFind tp1 with
            | Some tpTy1 -> SolveTypeSubsumesType csenv ndeep m2 trace cxsln tpTy1 ty2
            | _ ->
            match sty2 with
            | TType_var (r2, nullness2) when typarEq tp1 r2 -> 
                SolveNullnessEquiv csenv m2 trace ty1 ty2 nullness1 nullness2
            | TType_var (r2, nullness2)  when not csenv.MatchingOnly ->
                trackErrors {
                    do! SolveTyparSubtypeOfType csenv ndeep m2 trace r2 ty1
                    let nullnessAfterSolution2 = combineNullness (nullnessOfTy g sty2) nullness2
                    do! SolveNullnessSubsumesNullness csenv m2 trace ty1 ty2 nullness1 nullnessAfterSolution2
                }
            | _ ->  SolveTypeEqualsTypeKeepAbbrevsWithCxsln csenv ndeep m2 trace cxsln ty1 ty2

        | _, TType_var (r2, nullness2) when not csenv.MatchingOnly ->
            trackErrors {
                do! SolveTyparSubtypeOfType csenv ndeep m2 trace r2 ty1
                let nullnessAfterSolution2 = combineNullness (nullnessOfTy g sty2) nullness2
                do! SolveNullnessSubsumesNullness csenv m2 trace ty1 ty2 (nullnessOfTy g sty1) nullnessAfterSolution2
            }

        | TType_tuple (tupInfo1, l1), TType_tuple (tupInfo2, l2) -> 
            if evalTupInfoIsStruct tupInfo1 <> evalTupInfoIsStruct tupInfo2 then
                ErrorD (ConstraintSolverError(FSComp.SR.tcTupleStructMismatch(), csenv.m, m2))
            else
                SolveTypeEqualsTypeEqns csenv ndeep m2 trace cxsln l1 l2 (* nb. can unify since no variance *)
        | TType_fun (domainTy1, rangeTy1, nullness1), TType_fun (domainTy2, rangeTy2, nullness2) ->
            // nb. can unify since no variance
            trackErrors {
                do! SolveFunTypeEqn csenv ndeep m2 trace cxsln domainTy1 domainTy2 rangeTy1 rangeTy2
                do! SolveNullnessSubsumesNullness csenv m2 trace ty1 ty2 nullness1 nullness2
            }
        | TType_anon (anonInfo1, l1), TType_anon (anonInfo2, l2) ->
            trackErrors {
                do! SolveAnonInfoEqualsAnonInfo csenv m2 anonInfo1 anonInfo2
                do! SolveTypeEqualsTypeEqns csenv ndeep m2 trace cxsln l1 l2
            }
        | TType_measure ms1, TType_measure ms2 ->
            UnifyMeasures csenv trace ms1 ms2

        // Enforce the identities float=float<1>, float32=float32<1> and decimal=decimal<1> 
        | _, TType_app (tc2, [ms2], _) when tc2.IsMeasureableReprTycon && typeEquiv csenv.g sty1 (reduceTyconRefMeasureableOrProvided csenv.g tc2 [ms2]) ->
            trackErrors {
                do! SolveTypeEqualsTypeKeepAbbrevsWithCxsln csenv ndeep m2 trace cxsln ms2 (TType_measure(Measure.One m2))
                do! SolveNullnessSubsumesNullness csenv m2 trace ty1 ty2 (nullnessOfTy g sty1) (nullnessOfTy g sty2)
            }

        | TType_app (tc1, [ms1], _), _ when tc1.IsMeasureableReprTycon && typeEquiv csenv.g sty2 (reduceTyconRefMeasureableOrProvided csenv.g tc1 [ms1]) ->
            trackErrors {
                do! SolveTypeEqualsTypeKeepAbbrevsWithCxsln csenv ndeep m2 trace cxsln ms1 (TType_measure(Measure.One m2))
                do! SolveNullnessSubsumesNullness csenv m2 trace ty1 ty2 (nullnessOfTy g sty1) (nullnessOfTy g sty2)
            }

        // Special subsumption rule for byref tags
        | TType_app (tc1, l1, _)  , TType_app (tc2, l2, _) when tyconRefEq g tc1 tc2  && g.byref2_tcr.CanDeref && tyconRefEq g g.byref2_tcr tc1 ->
            match l1, l2 with
            | [ h1; tag1 ], [ h2; tag2 ] -> trackErrors {
                do! SolveTypeEqualsType csenv ndeep m2 trace None h1 h2
                match stripTyEqnsA csenv.g canShortcut tag1, stripTyEqnsA csenv.g canShortcut tag2 with
                | TType_app(tagc1, [], _), TType_app(tagc2, [], _)
                    when (tyconRefEq g tagc2 g.byrefkind_InOut_tcr &&
                            (tyconRefEq g tagc1 g.byrefkind_In_tcr || tyconRefEq g tagc1 g.byrefkind_Out_tcr) ) -> ()
                | _ -> return! SolveTypeEqualsType csenv ndeep m2 trace cxsln tag1 tag2
                }
            | _ -> SolveTypeEqualsTypeWithContravarianceEqns csenv ndeep m2 trace cxsln l1 l2 tc1.TyparsNoRange tc1

        | TType_app (tc1, l1, _)  , TType_app (tc2, l2, _) when tyconRefEq g tc1 tc2  ->
            trackErrors {            
                do! SolveTypeEqualsTypeWithContravarianceEqns csenv ndeep m2 trace cxsln l1 l2 tc1.TyparsNoRange tc1
                do! SolveNullnessSubsumesNullness csenv m2 trace ty1 ty2 (nullnessOfTy g sty1) (nullnessOfTy g sty2)
            }

        | TType_ucase (uc1, l1), TType_ucase (uc2, l2) when g.unionCaseRefEq uc1 uc2  -> 
            SolveTypeEqualsTypeEqns csenv ndeep m2 trace cxsln l1 l2

        | _ ->
            // By now we know the type is not a variable type 
            // C :> obj ---> <solved> 
            if isObjNullTy g ty1 then 
                CompleteD 
            else        
                let m = csenv.m
                // 'a[] :> IList<'b>   ---> 'a = 'b  
                // 'a[] :> ICollection<'b>   ---> 'a = 'b  
                // 'a[] :> IEnumerable<'b>   ---> 'a = 'b  
                // 'a[] :> IReadOnlyList<'b>   ---> 'a = 'b  
                // 'a[] :> IReadOnlyCollection<'b>   ---> 'a = 'b  
                // Note we don't support co-variance on array types nor 
                // the special .NET conversions for these types 
                match ty1 with
                | AppTy g (tcref1, tinst1) when
                    isArray1DTy g ty2 &&
                        (tyconRefEq g tcref1 g.tcref_System_Collections_Generic_IList || 
                            tyconRefEq g tcref1 g.tcref_System_Collections_Generic_ICollection || 
                            tyconRefEq g tcref1 g.tcref_System_Collections_Generic_IReadOnlyList || 
                            tyconRefEq g tcref1 g.tcref_System_Collections_Generic_IReadOnlyCollection || 
                            tyconRefEq g tcref1 g.tcref_System_Collections_Generic_IEnumerable) ->
                    match tinst1 with 
                    | [elemTy1] -> 
                        let elemTy2 = destArrayTy g ty2
                        SolveTypeEqualsTypeKeepAbbrevsWithCxsln csenv ndeep m2 trace cxsln elemTy1 elemTy2
                    | _ -> error(InternalError("destArrayTy", m))

                | _ ->
                    // D<inst> :> Head<_> --> C<inst'> :> Head<_> for the 
                    // first interface or super-class C supported by D which 
                    // may feasibly convert to Head. 
                    match FindUniqueFeasibleSupertype g amap m ty1 ty2 with 
                    | None -> ErrorD(ConstraintSolverTypesNotInSubsumptionRelation(denv, ty1, ty2, m, m2))
                    | Some t -> SolveTypeSubsumesType csenv ndeep m2 trace cxsln ty1 t

and SolveTypeSubsumesTypeKeepAbbrevs csenv ndeep m2 trace cxsln ty1 ty2 = 
   let denv = csenv.DisplayEnv
   TryD (fun () -> SolveTypeSubsumesType csenv ndeep m2 trace cxsln ty1 ty2)
        (function 
         | LocallyAbortOperationThatLosesAbbrevs -> ErrorD(ConstraintSolverTypesNotInSubsumptionRelation(denv, ty1, ty2, csenv.m, m2))
         | err -> ErrorD err)

//-------------------------------------------------------------------------
// Solve and record non-equality constraints
//------------------------------------------------------------------------- 

and SolveTyparSubtypeOfType (csenv: ConstraintSolverEnv) ndeep m2 trace tp ty1 = 
    let g = csenv.g
    if isObjNullTy g ty1 then 
        CompleteD
    elif isObjTyAnyNullness g ty1 then
        AddConstraint csenv ndeep m2 trace tp (TyparConstraint.NotSupportsNull csenv.m)
    elif typeEquiv g ty1 (mkTyparTy tp) then 
        CompleteD
    elif isSealedTy g ty1 then 
        SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace (mkTyparTy tp) ty1
    else
        if SubtypeConstraintImplied g tp.Constraints ty1 then
            CompleteD
        else
            AddConstraint csenv ndeep m2 trace tp (TyparConstraint.CoercesTo(ty1, csenv.m))

and DepthCheck ndeep m =
    if ndeep > 300 then
        error(Error(FSComp.SR.csTypeInferenceMaxDepth(), m))
    else
        CompleteD

// If this is a type that's parameterized on a unit-of-measure (expected to be numeric), unify its measure with 1
and SolveDimensionlessNumericType (csenv: ConstraintSolverEnv) ndeep m2 trace ty =
    match getMeasureOfType csenv.g ty with
    | Some (tcref, _) -> 
        SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace ty (mkWoNullAppTy tcref [TType_measure(Measure.One m2)])
    | None ->
        CompleteD

/// Attempt to solve a statically resolved member constraint.
///
/// 1. We do a bunch of fakery to pretend that primitive types have certain members. 
///    We pretend int and other types support a number of operators.  In the actual IL for mscorlib they 
///    don't. The type-directed static optimization rules in the library code that makes use of this 
///    will deal with the problem. 
///
/// 2. Some additional solutions are forced prior to generalization (permitWeakResolution= Yes or YesDuringCodeGen). See above
and SolveMemberConstraint (csenv: ConstraintSolverEnv) ignoreUnresolvedOverload permitWeakResolution ndeep m2 trace traitInfo : OperationResult<bool> =
    trackErrors {
        let (TTrait(supportTys, nm, memFlags, traitObjAndArgTys, retTy, source, sln)) = traitInfo
        // Do not re-solve if already solved
        if sln.Value.IsSome then 
            return true 
        else
            let g = csenv.g
            let m = csenv.m
            let amap = csenv.amap
            let aenv = csenv.EquivEnv
            let denv = csenv.DisplayEnv

            let ndeep = ndeep + 1
            do! DepthCheck ndeep m

            // Remove duplicates from the set of types in the support 
            let supportTys = ListSet.setify (typeAEquiv g aenv) supportTys

            // Rebuild the trait info after removing duplicates 
            let traitInfo = traitInfo.WithSupportTypes supportTys
            let retTy = GetFSharpViewOfReturnType g retTy
        
            // Assert the object type if the constraint is for an instance member    
            if memFlags.IsInstance then 
                match supportTys, traitObjAndArgTys with
                | [ty], h :: _ -> do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace h ty 
                | _ -> do! ErrorD (ConstraintSolverError(FSComp.SR.csExpectedArguments(), m, m2))

            // Trait calls are only supported on pseudo type (variables)
            if not (g.langVersion.SupportsFeature LanguageFeature.InterfacesWithAbstractStaticMembers) then
                for e in supportTys do
                    do! SolveTypStaticReq csenv trace TyparStaticReq.HeadType e

            // SRTP constraints on rigid type parameters do not need to be solved
            let isRigid =
                supportTys |> List.forall (fun ty ->
                    match tryDestTyparTy g ty with
                    | ValueSome tp ->
                        match tp.Rigidity with
                        | TyparRigidity.Rigid
                        | TyparRigidity.WillBeRigid -> true
                        | _ -> false
                    | ValueNone -> false)

            let argTys = if memFlags.IsInstance then List.tail traitObjAndArgTys else traitObjAndArgTys 

            let minfos = GetRelevantMethodsForTrait csenv permitWeakResolution nm traitInfo

            let! res = 
                trackErrors {
                    match minfos, supportTys, memFlags.IsInstance, nm, argTys with
                    | _, _, false, ("op_Division" | "op_Multiply"), [argTy1;argTy2]
                        when 
                            // This simulates the existence of 
                            //    float * float -> float
                            //     float32 * float32 -> float32
                            //    float<'u> * float<'v> -> float<'u 'v>
                            //    float32<'u> * float32<'v> -> float32<'u 'v>
                            //    decimal<'u> * decimal<'v> -> decimal<'u 'v>
                            //    decimal<'u> * decimal -> decimal<'u>
                            //    float32<'u> * float32<'v> -> float32<'u 'v>
                            //    int * int -> int
                            //    int64 * int64 -> int64
                            //
                            // The rule is triggered by these sorts of inputs when permitWeakResolution=false
                            //    float * float 
                            //    float * float32 // will give error 
                            //    decimal<m> * decimal<m>
                            //    decimal<m> * decimal  <-- Note this one triggers even though "decimal" has some possibly-relevant methods
                            //    float * Matrix // the rule doesn't trigger for this one since Matrix has overloads we can use and we prefer those instead
                            //    float * Matrix // the rule doesn't trigger for this one since Matrix has overloads we can use and we prefer those instead
                            //
                            // The rule is triggered by these sorts of inputs when permitWeakResolution=true
                            //    float * 'a 
                            //    'a * float 
                            //    decimal<'u> * 'a
                                (let checkRuleAppliesInPreferenceToMethods argTy1 argTy2 = 
                                    // Check that at least one of the argument types is numeric
                                    IsNumericOrIntegralEnumType g argTy1 && 
                                    // Check the other type is nominal, unless using weak resolution
                                    IsBinaryOpOtherArgType g permitWeakResolution argTy2 &&
                                    // This next condition checks that either 
                                    //   - Neither type contributes any methods OR
                                    //   - We have the special case "decimal<_> * decimal". In this case we have some 
                                    //     possibly-relevant methods from "decimal" but we ignore them in this case.
                                    (isNil minfos || (Option.isSome (getMeasureOfType g argTy1) && isDecimalTy g argTy2)) in

                                checkRuleAppliesInPreferenceToMethods argTy1 argTy2 || 
                                checkRuleAppliesInPreferenceToMethods argTy2 argTy1) ->

                        match getMeasureOfType g argTy1 with
                        | Some (tcref, ms1) ->
                            let ms2 = freshMeasure ()
                            do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy2 (mkWoNullAppTy tcref [TType_measure ms2])
                            do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy (mkWoNullAppTy tcref [TType_measure (Measure.Prod(ms1, (if nm = "op_Multiply" then ms2 else Measure.Inv ms2), unionRanges ms1.Range ms2.Range))])
                            return TTraitBuiltIn

                        | _ ->

                            match getMeasureOfType g argTy2 with
                            | Some (tcref, ms2) ->
                                let ms1 = freshMeasure ()
                                do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy1 (mkWoNullAppTy tcref [TType_measure ms1]) 
                                do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy (mkWoNullAppTy tcref [TType_measure (Measure.Prod(ms1, (if nm = "op_Multiply" then ms2 else Measure.Inv ms2), unionRanges ms1.Range ms2.Range))])
                                return TTraitBuiltIn

                            | _ ->

                                do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy2 argTy1
                                do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy argTy1
                                return TTraitBuiltIn

                    | _, _, false, ("op_Addition" | "op_Subtraction" | "op_Modulus"), [argTy1;argTy2] 
                        when // Ignore any explicit +/- overloads from any basic integral types
                            (minfos |> List.forall (fun (_, minfo) -> isIntegerTy g minfo.ApparentEnclosingType ) &&
                                (   IsAddSubModType nm g argTy1 && IsBinaryOpOtherArgType g permitWeakResolution argTy2
                                || IsAddSubModType nm g argTy2 && IsBinaryOpOtherArgType g permitWeakResolution argTy1)) -> 
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy2 argTy1
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy argTy1
                        return TTraitBuiltIn

                    | _, _, false, ("op_LessThan" | "op_LessThanOrEqual" | "op_GreaterThan" | "op_GreaterThanOrEqual" | "op_Equality" | "op_Inequality" ), [argTy1;argTy2] 
                        when // Ignore any explicit overloads from any basic integral types
                            (minfos |> List.forall (fun (_, minfo) -> isIntegerTy g minfo.ApparentEnclosingType ) &&
                                (   IsRelationalType g argTy1 && IsBinaryOpOtherArgType g permitWeakResolution argTy2
                                || IsRelationalType g argTy2 && IsBinaryOpOtherArgType g permitWeakResolution argTy1)) -> 
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy2 argTy1 
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy g.bool_ty
                        return TTraitBuiltIn

                    // We pretend for uniformity that the numeric types have a static property called Zero and One 
                    // As with constants, only zero is polymorphic in its units
                    | [], [ty], false, "get_Zero", [] 
                        when isNumericType g ty || isCharTy g ty -> 
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy ty
                        return TTraitBuiltIn

                    | [], [ty], false, "get_One", [] 
                        when isNumericType g ty || isCharTy g ty -> 
                        do! SolveDimensionlessNumericType csenv ndeep m2 trace ty 
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy ty
                        return TTraitBuiltIn

                    | [], _, false, "DivideByInt", [argTy1;argTy2] 
                        when isFpTy g argTy1 || isDecimalTy g argTy1 -> 
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy2 g.int_ty 
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy argTy1
                        return TTraitBuiltIn

                    // We pretend for uniformity that the 'string' and 'array' types have an indexer property called 'Item' 
                    | [], [ty], true, "get_Item", [argTy1] 
                        when isStringTy g ty -> 

                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy1 g.int_ty 
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy g.char_ty
                        return TTraitBuiltIn

                    | [], [ty], true, "get_Item", argTys
                        when isArrayTy g ty -> 

                        if rankOfArrayTy g ty <> argTys.Length then
                            do! ErrorD(ConstraintSolverError(FSComp.SR.csIndexArgumentMismatch((rankOfArrayTy g ty), argTys.Length), m, m2))

                        for argTy in argTys do
                            do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy g.int_ty

                        let ety = destArrayTy g ty
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy ety
                        return TTraitBuiltIn

                    | [], [ty], true, "set_Item", argTys
                        when isArrayTy g ty -> 
                    
                        if rankOfArrayTy g ty <> argTys.Length - 1 then
                            do! ErrorD(ConstraintSolverError(FSComp.SR.csIndexArgumentMismatch((rankOfArrayTy g ty), (argTys.Length - 1)), m, m2))
                        let argTys, lastTy = List.frontAndBack argTys

                        for argTy in argTys do
                            do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy g.int_ty

                        let elemTy = destArrayTy g ty
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace lastTy elemTy
                        return TTraitBuiltIn

                    | [], _, false, ("op_BitwiseAnd" | "op_BitwiseOr" | "op_ExclusiveOr"), [argTy1;argTy2] 
                        when    IsBitwiseOpType g argTy1 && IsBinaryOpOtherArgType g permitWeakResolution argTy2
                            || IsBitwiseOpType g argTy2 && IsBinaryOpOtherArgType g permitWeakResolution argTy1 -> 

                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy2 argTy1
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy argTy1
                        do! SolveDimensionlessNumericType csenv ndeep m2 trace argTy1
                        return TTraitBuiltIn

                    | [], _, false, ("op_LeftShift" | "op_RightShift"), [argTy1;argTy2] 
                        when    IsIntegerOrIntegerEnumTy g argTy1  -> 

                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy2 g.int_ty
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy argTy1
                        do! SolveDimensionlessNumericType csenv ndeep m2 trace argTy1
                        return TTraitBuiltIn

                    | _, _, false, "op_UnaryPlus", [argTy] 
                        when IsNumericOrIntegralEnumType g argTy -> 

                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy argTy
                        return TTraitBuiltIn

                    | _, _, false, "op_UnaryNegation", [argTy] 
                        when isSignedIntegerTy g argTy || isFpTy g argTy || isDecimalTy g argTy -> 

                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy argTy
                        return TTraitBuiltIn

                    | _, _, true, "get_Sign", [] 
                        when IsSignType g supportTys.Head ->

                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy g.int32_ty
                        return TTraitBuiltIn

                    | _, _, false, ("op_LogicalNot" | "op_OnesComplement"), [argTy] 
                        when IsIntegerOrIntegerEnumTy g argTy  -> 

                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy argTy
                        do! SolveDimensionlessNumericType csenv ndeep m2 trace argTy
                        return TTraitBuiltIn

                    | _, _, false, "Abs", [argTy] 
                        when isSignedIntegerTy g argTy || isFpTy g argTy || isDecimalTy g argTy -> 

                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy argTy
                        return TTraitBuiltIn

                    | _, _, false, "Sqrt", [argTy1] 
                        when isFpTy g argTy1 ->
                        match getMeasureOfType g argTy1 with
                            | Some (tcref, _) -> 
                                let ms1 = freshMeasure () 
                                do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy1 (mkWoNullAppTy tcref [TType_measure (Measure.Prod (ms1, ms1, ms1.Range))])
                                do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy (mkWoNullAppTy tcref [TType_measure ms1])
                                return TTraitBuiltIn
                            | None -> 
                                do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy argTy1
                                return TTraitBuiltIn

                    | _, _, false, ("Sin" | "Cos" | "Tan" | "Sinh" | "Cosh" | "Tanh" | "Atan" | "Acos" | "Asin" | "Exp" | "Ceiling" | "Floor" | "Round" | "Truncate" | "Log10" | "Log" | "Sqrt"), [argTy] 
                        when isFpTy g argTy -> 

                        do! SolveDimensionlessNumericType csenv ndeep m2 trace argTy
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy argTy
                        return TTraitBuiltIn

                    // Conversions from non-decimal numbers / strings / chars to non-decimal numbers / chars are built-in
                    | _, _, false, "op_Explicit", [argTy]
                        when (// The input type.
                                (IsNonDecimalNumericOrIntegralEnumType g argTy || isStringTy g argTy || isCharTy g argTy) &&
                                // The output type
                                (IsNonDecimalNumericOrIntegralEnumType g retTy || isCharTy g retTy)) ->

                        return TTraitBuiltIn

                    // Conversions from (including decimal) numbers / strings / chars to decimals are built-in
                    | _, _, false, "op_Explicit", [argTy]
                        when (// The input type.
                                (IsNumericOrIntegralEnumType g argTy || isStringTy g argTy || isCharTy g argTy) &&
                                // The output type
                                (isDecimalTy g retTy)) ->
                        return TTraitBuiltIn

                    // Conversions from decimal numbers to native integers are built-in
                    // The rest of decimal conversions are handled via op_Explicit lookup on System.Decimal (which also looks for op_Implicit)
                    | _, _, false, "op_Explicit", [argTy]
                        when (// The input type.
                                (isDecimalTy g argTy) &&
                                // The output type
                                (isNativeIntegerTy g retTy)) ->
                        return TTraitBuiltIn

                    | [], _, false, "Pow", [argTy1; argTy2] 
                        when isFpTy g argTy1 -> 
                    
                        do! SolveDimensionlessNumericType csenv ndeep m2 trace argTy1
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy2 argTy1
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy argTy1
                        return TTraitBuiltIn

                    | _, _, false, "Atan2", [argTy1; argTy2] 
                        when isFpTy g argTy1 -> 
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argTy2 argTy1
                        match getMeasureOfType g argTy1 with
                        | None -> do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy argTy1
                        | Some (tcref, ms) -> do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy (mkWoNullAppTy tcref [TType_measure(Measure.One ms.Range)])
                        return TTraitBuiltIn

                    | _ -> 
                        // OK, this is not solved by a built-in constraint.
                        // Now look for real solutions

                        // First look for a solution by a record property
                        let recdPropSearch = 
                            let isGetProp = nm.StartsWithOrdinal("get_") 
                            let isSetProp = nm.StartsWithOrdinal("set_") 
                            if not isRigid && ((argTys.IsEmpty && isGetProp) || isSetProp) then
                                let propName = nm[4..]
                                let props = 
                                    supportTys |> List.choose (fun ty ->
                                        match TryFindIntrinsicNamedItemOfType csenv.InfoReader (propName, AccessibleFromEverywhere, false) FindMemberFlag.IgnoreOverrides m ty with
                                        | Some (RecdFieldItem rfinfo) 
                                            when (isGetProp || rfinfo.RecdField.IsMutable) && 
                                                (rfinfo.IsStatic = not memFlags.IsInstance) && 
                                                IsRecdFieldAccessible amap m AccessibleFromEverywhere rfinfo.RecdFieldRef &&
                                                not rfinfo.LiteralValue.IsSome && 
                                                not rfinfo.RecdField.IsCompilerGenerated -> 
                                            Some (rfinfo, isSetProp)
                                        | _ -> None)
                                match props with 
                                | [ prop ] -> Some prop
                                | _ -> None
                            else
                                None

                        let anonRecdPropSearch = 
                            let isGetProp = nm.StartsWithOrdinal("get_")
                            if not isRigid && isGetProp && memFlags.IsInstance  then
                                let propName = nm[4..]
                                let props = 
                                    supportTys |> List.choose (fun ty ->
                                        match TryFindAnonRecdFieldOfType g ty propName with
                                        | Some (Item.AnonRecdField(anonInfo, tinst, i, _)) -> Some (anonInfo, tinst, i)
                                        | _ -> None)
                                match props with 
                                | [ prop ] -> Some prop
                                | _ -> None
                            else
                                None

                        // Now check if there are no feasible solutions at all
                        match minfos, recdPropSearch, anonRecdPropSearch with 
                        | [], None, None when MemberConstraintIsReadyForStrongResolution csenv traitInfo ->
                            if supportTys |> List.exists (isFunTy g) then
                                return! ErrorD (ConstraintSolverError(FSComp.SR.csExpectTypeWithOperatorButGivenFunction(ConvertValLogicalNameToDisplayNameCore nm), m, m2))
                            elif supportTys |> List.exists (isAnyTupleTy g) then
                                return! ErrorD (ConstraintSolverError(FSComp.SR.csExpectTypeWithOperatorButGivenTuple(ConvertValLogicalNameToDisplayNameCore nm), m, m2))
                            else
                                match nm, argTys with 
                                | "op_Explicit", [argTy] ->
                                    let argTyString = NicePrint.prettyStringOfTy denv argTy
                                    let rtyString = NicePrint.prettyStringOfTy denv retTy
                                    return! ErrorD (ConstraintSolverError(FSComp.SR.csTypeDoesNotSupportConversion(argTyString, rtyString), m, m2))
                                | _ -> 
                                    let tyString = 
                                        match supportTys with
                                        | [ty] -> NicePrint.minimalStringOfType denv ty
                                        | _ -> supportTys |> List.map (NicePrint.minimalStringOfType denv) |> String.concat ", "
                                    let opName = ConvertValLogicalNameToDisplayNameCore nm
                                    let err = 
                                        match opName with 
                                        | "?>="  | "?>"  | "?<="  | "?<"  | "?="  | "?<>" 
                                        | ">=?"  | ">?"  | "<=?"  | "<?"  | "=?"  | "<>?" 
                                        | "?>=?" | "?>?" | "?<=?" | "?<?" | "?=?" | "?<>?" ->
                                            if List.isSingleton supportTys then FSComp.SR.csTypeDoesNotSupportOperatorNullable(tyString, opName)
                                            else FSComp.SR.csTypesDoNotSupportOperatorNullable(tyString, opName)
                                        | _ ->
                                            match supportTys, source.Value with
                                            | [_], Some s when s.StartsWith("Operators.") ->
                                                let opSource = s[10..]
                                                if opSource = nm then FSComp.SR.csTypeDoesNotSupportOperator(tyString, opName)
                                                else FSComp.SR.csTypeDoesNotSupportOperator(tyString, opSource)
                                            | [_], Some s ->
                                                FSComp.SR.csFunctionDoesNotSupportType(s, tyString, nm)
                                            | [_], _
                                                -> FSComp.SR.csTypeDoesNotSupportOperator(tyString, opName)
                                            | _, _ 
                                                -> FSComp.SR.csTypesDoNotSupportOperator(tyString, opName)
                                    return! ErrorD(ConstraintSolverError(err, m, m2))

                        | _ -> 
                            let dummyExpr = mkUnit g m
                            let calledMethGroup = 
                                minfos
                                    // curried members may not be used to satisfy constraints
                                    |> List.choose (fun (staticTy, minfo) ->
                                        if minfo.IsCurried then
                                            None
                                        else
                                            let callerArgs =
                                                {
                                                    Unnamed = [ (argTys |> List.map (fun argTy -> CallerArg(argTy, m, false, dummyExpr))) ]
                                                    Named = [ [ ] ]
                                                }
                                            let minst = FreshenMethInfo m minfo
                                            let objtys = minfo.GetObjArgTypes(amap, m, minst)
                                            Some(CalledMeth<Expr>(csenv.InfoReader, None, false, FreshenMethInfo, m, AccessibleFromEverywhere, minfo, minst, minst, None, objtys, callerArgs, false, false, None, Some staticTy)))

                            let methOverloadResult, errors = 
                                trace.CollectThenUndoOrCommit
                                    (fun (a, _) -> Option.isSome a)
                                    (fun trace -> ResolveOverloading csenv (WithTrace trace) nm ndeep (Some traitInfo) CallerArgs.Empty AccessibleFromEverywhere calledMethGroup false (Some (MustEqual retTy)))

                            match anonRecdPropSearch, recdPropSearch, methOverloadResult with 
                            | Some (anonInfo, tinst, i), None, None -> 
                                // OK, the constraint is solved by a record property. Assert that the return types match.
                                let rty2 = List.item i tinst
                                do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy rty2
                                return TTraitSolvedAnonRecdProp(anonInfo, tinst, i)

                            | None, Some (rfinfo, isSetProp), None -> 
                                // OK, the constraint is solved by a record property. Assert that the return types match.
                                let rty2 = if isSetProp then g.unit_ty else rfinfo.FieldType
                                do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy rty2
                                return TTraitSolvedRecdProp(rfinfo, isSetProp)

                            | None, None, Some (calledMeth: CalledMeth<_>) -> 
                                // OK, the constraint is solved.
                                let minfo = calledMeth.Method

                                do! errors
                                let isInstance = minfo.IsInstance
                                if isInstance <> memFlags.IsInstance then 
                                    return!
                                        if isInstance then
                                            ErrorD(ConstraintSolverError(FSComp.SR.csMethodFoundButIsNotStatic((NicePrint.minimalStringOfType denv minfo.ApparentEnclosingType), (ConvertValLogicalNameToDisplayNameCore nm), nm), m, m2 ))
                                        else
                                            ErrorD(ConstraintSolverError(FSComp.SR.csMethodFoundButIsStatic((NicePrint.minimalStringOfType denv minfo.ApparentEnclosingType), (ConvertValLogicalNameToDisplayNameCore nm), nm), m, m2 ))
                                else 
                                    do! CheckMethInfoAttributes g m None minfo
                                    return TTraitSolved (minfo, calledMeth.CalledTyArgs, calledMeth.OptionalStaticType)

                            | _ -> 
                                do! AddUnsolvedMemberConstraint csenv ndeep m2 trace permitWeakResolution ignoreUnresolvedOverload traitInfo errors
                                return TTraitUnsolved
                    }
            return! RecordMemberConstraintSolution csenv.SolverState m trace traitInfo res
    }

and AddUnsolvedMemberConstraint csenv ndeep m2 trace permitWeakResolution ignoreUnresolvedOverload traitInfo errors =
    trackErrors {
        let g = csenv.g

        let nm = traitInfo.MemberLogicalName
        let supportTypars = GetTyparSupportOfMemberConstraint csenv traitInfo
        let frees = GetFreeTyparsOfMemberConstraint csenv traitInfo

        // Trait calls are only supported on pseudo type (variables) unless supported by IWSAM constraints
        //
        // SolveTypStaticReq is applied here if IWSAMs are supported
        if g.langVersion.SupportsFeature LanguageFeature.InterfacesWithAbstractStaticMembers then
            for supportTypar in supportTypars do
                if not (SupportTypeOfMemberConstraintIsSolved csenv traitInfo supportTypar) then
                    do! SolveTypStaticReqTypar csenv trace TyparStaticReq.HeadType supportTypar

        // If there's nothing left to learn then raise the errors.
        // Note: we should likely call MemberConstraintIsReadyForResolution here when permitWeakResolution=false but for stability
        // reasons we use the more restrictive isNil frees.
        if (permitWeakResolution.Permit && MemberConstraintIsReadyForWeakResolution csenv traitInfo) || isNil frees then
            do! errors
        // Otherwise re-record the trait waiting for canonicalization
        else
            do! AddMemberConstraint csenv ndeep m2 trace traitInfo supportTypars frees

        match errors with
        | ErrorResult (_, UnresolvedOverloading _)
            when
                not ignoreUnresolvedOverload &&
                csenv.ErrorOnFailedMemberConstraintResolution &&
                (not (nm = "op_Explicit" || nm = "op_Implicit")) ->
            return! ErrorD AbortForFailedMemberConstraintResolution
        | _ ->
            ()
  }

/// Record the solution to a member constraint in the mutable reference cell attached to 
/// each member constraint.
and RecordMemberConstraintSolution css m trace traitInfo traitConstraintSln =
    match traitConstraintSln with
    | TTraitUnsolved -> 
        ResultD false

    | TTraitSolved (minfo, minst, staticTyOpt) ->
        let sln = MemberConstraintSolutionOfMethInfo css m minfo minst staticTyOpt
        TransactMemberConstraintSolution traitInfo trace sln
        ResultD true

    | TTraitBuiltIn -> 
        TransactMemberConstraintSolution traitInfo trace BuiltInSln
        ResultD true

    | TTraitSolvedRecdProp (rfinfo, isSet) -> 
        let sln = FSRecdFieldSln(rfinfo.TypeInst,rfinfo.RecdFieldRef,isSet)
        TransactMemberConstraintSolution traitInfo trace sln
        ResultD true

    | TTraitSolvedAnonRecdProp (anonInfo, tinst, i) -> 
        let sln = FSAnonRecdFieldSln(anonInfo, tinst, i)
        TransactMemberConstraintSolution traitInfo trace sln
        ResultD true

/// Convert a MethInfo into the data we save in the TAST
and MemberConstraintSolutionOfMethInfo css m minfo minst staticTyOpt =
#if !NO_TYPEPROVIDERS
#else
    // to prevent unused parameter warning
    ignore css
#endif
    match minfo with 
    | ILMeth(_, ilMeth, _) ->
       let mref = IL.mkRefToILMethod (ilMeth.DeclaringTyconRef.CompiledRepresentationForNamedType, ilMeth.RawMetadata)
       let iltref = ilMeth.ILExtensionMethodDeclaringTyconRef |> Option.map (fun tcref -> tcref.CompiledRepresentationForNamedType)
       ILMethSln(ilMeth.ApparentEnclosingType, iltref, mref, minst, staticTyOpt)

    | FSMeth(_, ty, vref, _) ->  
       FSMethSln(ty, vref, minst, staticTyOpt)

    | MethInfoWithModifiedReturnType(mi,_) -> MemberConstraintSolutionOfMethInfo css m mi minst staticTyOpt

    | MethInfo.DefaultStructCtor _ -> 
       error(InternalError("the default struct constructor was the unexpected solution to a trait constraint", m))

#if !NO_TYPEPROVIDERS
    | ProvidedMeth(amap, mi, _, m) -> 
        let g = amap.g
        let minst = []   // GENERIC TYPE PROVIDERS: for generics, we would have an minst here
        let allArgVars, allArgs = minfo.GetParamTypes(amap, m, minst) |> List.concat |> List.mapi (fun i ty -> mkLocal m ("arg"+string i) ty) |> List.unzip
        let objArgVars, objArgs = (if minfo.IsInstance then [mkLocal m "this" minfo.ApparentEnclosingType] else []) |> List.unzip
        let callMethInfoOpt, callExpr, callExprTy = ProvidedMethodCalls.BuildInvokerExpressionForProvidedMethodCall css.TcVal (g, amap, mi, objArgs, NeverMutates, false, ValUseFlag.NormalValUse, allArgs, m) 
        let closedExprSln = ClosedExprSln (mkLambdas g m [] (objArgVars@allArgVars) (callExpr, callExprTy) )

        // If the call is a simple call to an IL method with all the arguments in the natural order, then revert to use ILMethSln.
        // This is important for calls to operators on generated provided types. There is an (unchecked) condition
        // that generative providers do not re=order arguments or insert any more information into operator calls.
        match callMethInfoOpt, callExpr with 
        | Some methInfo, Expr.Op (TOp.ILCall (_, _, _, _, NormalValUse, _, _, ilMethRef, _, methInst, _), [], args, m)
             when (args, (objArgVars@allArgVars)) ||> List.lengthsEqAndForall2 (fun a b -> match a with Expr.Val (v, _, _) -> valEq v.Deref b | _ -> false) ->
                let declaringTy = ImportProvidedType amap m (methInfo.PApply((fun x -> nonNull<ProvidedType> x.DeclaringType), m))
                if isILAppTy g declaringTy then 
                    let extOpt = None  // EXTENSION METHODS FROM TYPE PROVIDERS: for extension methods coming from the type providers we would have something here.
                    ILMethSln(declaringTy, extOpt, ilMethRef, methInst, staticTyOpt)
                else
                    closedExprSln
        | _ -> 
                closedExprSln

#endif

/// Write into the reference cell stored in the TAST and add to the undo trace if necessary
and TransactMemberConstraintSolution traitInfo (trace: OptionalTrace) sln  =
    let prev = traitInfo.Solution 
    trace.Exec (fun () -> traitInfo.Solution <- Some sln) (fun () -> traitInfo.Solution <- prev)

/// Only consider overload resolution if canonicalizing or all the types are now nominal. 
/// That is, don't perform resolution if more nominal information may influence the set of available overloads 
and GetRelevantMethodsForTrait (csenv: ConstraintSolverEnv) (permitWeakResolution: PermitWeakResolution) nm traitInfo : (TType * MethInfo) list =
    let results = 
        if permitWeakResolution.Permit || MemberConstraintSupportIsReadyForDeterminingOverloads csenv traitInfo then
            let m = csenv.m

            let nominalTys = GetNominalSupportOfMemberConstraint csenv nm traitInfo

            let minfos =
                [ for (supportTy, nominalTy) in nominalTys do
                    let infos =
                        match traitInfo.MemberFlags.MemberKind with
                        | SynMemberKind.Constructor ->
                            GetIntrinsicConstructorInfosOfType csenv.SolverState.InfoReader m nominalTy
                        | _ ->
                            GetIntrinsicMethInfosOfType csenv.SolverState.InfoReader (Some nm) AccessibleFromSomeFSharpCode AllowMultiIntfInstantiations.Yes IgnoreOverrides m nominalTy
                    for info in infos do
                        supportTy, info ]

            // Merge the sets so we don't get the same minfo from each side 
            // We merge based on whether minfos use identical metadata or not. 
            let minfos = ListSet.setify (fun (_,minfo1) (_, minfo2) -> MethInfo.MethInfosUseIdenticalDefinitions minfo1 minfo2) minfos
            
            /// Check that the available members aren't hiding a member from the parent (depth 1 only)
            let relevantMinfos = minfos |> List.filter(fun (_, minfo) -> not minfo.IsDispatchSlot && not minfo.IsVirtual && minfo.IsInstance)
            minfos
            |> List.filter(fun (_, minfo1) ->
                not(minfo1.IsDispatchSlot && 
                    relevantMinfos
                    |> List.exists (fun (_, minfo2) -> MethInfosEquivByNameAndSig EraseAll true csenv.g csenv.amap m minfo2 minfo1)))
        else 
            []

    // The trait name "op_Explicit" also covers "op_Implicit", so look for that one too.
    if nm = "op_Explicit" then 
        let traitInfo2 = traitInfo.WithMemberName "op_Implicit"
        results @ GetRelevantMethodsForTrait csenv permitWeakResolution "op_Implicit" traitInfo2
    else
        results


/// The typar support of the member constraint.
and GetTyparSupportOfMemberConstraint csenv traitInfo =
    traitInfo.SupportTypes |> List.choose (tryAnyParTyOption csenv.g)
    
/// The nominal types supporting the solution of a particular named SRTP constraint.
/// Constraints providing interfaces with static abstract methods can be
/// used to solve SRTP static member constraints on type parameters.
and GetNominalSupportOfMemberConstraint csenv nm traitInfo =
    let m = csenv.m
    let g = csenv.g
    let infoReader = csenv.InfoReader
    [ for supportTy in traitInfo.SupportTypes do
        if isTyparTy g supportTy then
            let mutable replaced = false
            for cx in (destTyparTy g supportTy).Constraints do
                match cx with
                | TyparConstraint.CoercesTo(interfaceTy, _) when infoReader.IsInterfaceTypeWithMatchingStaticAbstractMember m nm AccessibleFromSomeFSharpCode interfaceTy ->
                    replaced <- true
                    (supportTy, interfaceTy)
                | _ -> ()
            if not replaced then
                (supportTy, supportTy)
        else
            (supportTy, supportTy) ]

and SupportTypeHasInterfaceWithMatchingStaticAbstractMember (csenv: ConstraintSolverEnv) (traitInfo: TraitConstraintInfo) (supportTyPar: Typar) =
    let g = csenv.g
    let m = csenv.m
    let infoReader = csenv.InfoReader

    if g.langVersion.SupportsFeature LanguageFeature.InterfacesWithAbstractStaticMembers then
        let mutable found = false
        for cx in supportTyPar.Constraints do
            match cx with
            | TyparConstraint.CoercesTo(interfaceTy, _) when infoReader.IsInterfaceTypeWithMatchingStaticAbstractMember m traitInfo.MemberLogicalName AccessibleFromSomeFSharpCode interfaceTy ->
                found <- true
            | _ -> ()
        found
    else
        false

and SupportTypeOfMemberConstraintIsSolved (csenv: ConstraintSolverEnv) (traitInfo: TraitConstraintInfo) supportTypar =
    SupportTypeHasInterfaceWithMatchingStaticAbstractMember csenv traitInfo supportTypar

// This may be relevant to future bug fixes, see https://github.com/dotnet/fsharp/issues/3814
// /// Check if some part of the support is solved.  
// and SupportOfMemberConstraintIsPartiallySolved (csenv: ConstraintSolverEnv) (TTrait(tys, _, _, _, _, _)) =
//     tys |> List.exists (isAnyParTy csenv.g >> not)
    
/// Get all the unsolved typars (statically resolved or not) relevant to the member constraint
and GetFreeTyparsOfMemberConstraint (csenv: ConstraintSolverEnv) traitInfo =
    let (TTrait(tys=supportTys; objAndArgTys=argTys; returnTyOpt=retTy)) = traitInfo
    freeInTypesLeftToRightSkippingConstraints csenv.g (supportTys @ argTys @ Option.toList retTy)

and MemberConstraintIsReadyForWeakResolution csenv traitInfo =
   SupportOfMemberConstraintIsFullySolved csenv traitInfo

and MemberConstraintIsReadyForStrongResolution csenv traitInfo =
   SupportOfMemberConstraintIsFullySolved csenv traitInfo

and MemberConstraintSupportIsReadyForDeterminingOverloads csenv traitInfo =
   SupportOfMemberConstraintIsFullySolved csenv traitInfo ||
   // Left-bias for SRTP constraints where the first is constrained by an IWSAM type. This is because typical IWSAM hierarchies
   // such as System.Numerics hierarchy math are left-biased.
   (match traitInfo.SupportTypes with
    | firstSupportTy :: _ -> isAnyParTy csenv.g firstSupportTy && SupportTypeHasInterfaceWithMatchingStaticAbstractMember csenv traitInfo (destAnyParTy csenv.g firstSupportTy)
    | _ -> false)

/// Check if the support is fully solved.
and SupportOfMemberConstraintIsFullySolved (csenv: ConstraintSolverEnv) traitInfo =
    let g = csenv.g
    traitInfo.SupportTypes |> List.forall (fun ty -> if isAnyParTy g ty then SupportTypeOfMemberConstraintIsSolved csenv traitInfo (destAnyParTy g ty) else true)

/// Re-solve the global constraints involving any of the given type variables. 
/// Trait constraints can't always be solved using the pessimistic rules. We only canonicalize 
/// them forcefully (permitWeakResolution=true) prior to generalization. 
and SolveRelevantMemberConstraints (csenv: ConstraintSolverEnv) ndeep permitWeakResolution trace tps =
    RepeatWhileD ndeep
        (fun ndeep -> 
            tps 
            |> AtLeastOneD (fun tp -> 
                /// Normalize the typar 
                let ty = mkTyparTy tp
                match tryAnyParTy csenv.g ty with
                | ValueSome tp ->
                    SolveRelevantMemberConstraintsForTypar csenv ndeep permitWeakResolution trace tp
                | ValueNone -> 
                    ResultD false)) 

and SolveRelevantMemberConstraintsForTypar (csenv: ConstraintSolverEnv) ndeep permitWeakResolution (trace: OptionalTrace) tp =
    let cxst = csenv.SolverState.ExtraCxs
    let tpn = tp.Stamp
    let cxs = cxst.FindAll tpn
    if isNil cxs then ResultD false else
    
    trace.Exec (fun () -> cxs |> List.iter (fun _ -> cxst.Remove tpn)) (fun () -> cxs |> List.iter (fun cx -> cxst.Add(tpn, cx)))
    assert (isNil (cxst.FindAll tpn)) 

    cxs 
    |> AtLeastOneD (fun (traitInfo, m2) -> 
        let csenv = { csenv with m = m2 }
        SolveMemberConstraint csenv true permitWeakResolution (ndeep+1) m2 trace traitInfo)

and CanonicalizeRelevantMemberConstraints (csenv: ConstraintSolverEnv) ndeep trace tps =
    SolveRelevantMemberConstraints csenv ndeep PermitWeakResolution.Yes trace tps
  
and AddMemberConstraint (csenv: ConstraintSolverEnv) ndeep m2 (trace: OptionalTrace) traitInfo support (frees: Typar list) =
    let g = csenv.g
    let aenv = csenv.EquivEnv
    let cxst = csenv.SolverState.ExtraCxs

    // Write the constraint into the global table. That is, 
    // associate the constraint with each type variable in the free variables of the constraint.
    // This will mean the constraint gets resolved whenever one of these free variables gets solved.
    frees 
    |> List.iter (fun tp -> 
        let tpn = tp.Stamp

        let cxs = cxst.FindAll tpn

        // check the constraint is not already listed for this type variable
        if not (cxs |> List.exists (fun (traitInfo2, _) -> traitsAEquiv g aenv traitInfo traitInfo2)) then 
            trace.Exec (fun () -> csenv.SolverState.ExtraCxs.Add (tpn, (traitInfo, m2))) (fun () -> csenv.SolverState.ExtraCxs.Remove tpn)
    )

    // Associate the constraint with each type variable in the support, so if the type variable
    // gets generalized then this constraint is attached at the binding site.
    trackErrors {
        for tp in support do
            do! AddConstraint csenv ndeep m2 trace tp (TyparConstraint.MayResolveMember(traitInfo, m2))
    }

    
and TraitsAreRelated (csenv: ConstraintSolverEnv) retry traitInfo1 traitInfo2 =
    let g = csenv.g
    let (TTrait(tys=tys1; memberName=nm1; memberFlags=memFlags1; objAndArgTys=argTys1)) = traitInfo1
    let (TTrait(tys=tys2; memberName=nm2; memberFlags=memFlags2; objAndArgTys=argTys2)) = traitInfo2
    memFlags1.IsInstance = memFlags2.IsInstance &&
    nm1 = nm2 &&
    // Multiple op_Explicit and op_Implicit constraints can exist for the same type variable.
    // See FSharp 1.0 bug 6477.
    not (nm1 = "op_Explicit" || nm1 = "op_Implicit") &&
    argTys1.Length = argTys2.Length &&
    (List.lengthsEqAndForall2 (typeEquiv g) tys1 tys2 || retry)

// Type variable sets may not have two trait constraints with the same name, nor
// be constrained by different instantiations of the same interface type.
//
// This results in limitations on generic code, especially "inline" code, which
// may require type annotations.
//
// The 'retry' flag is passed when a rigid type variable is about to raise a missing constraint error
// and the lengths of the support types are not equal (i.e. one is length 1, the other is length 2).
// In this case the support types are first forced to be equal.
and EnforceConstraintConsistency (csenv: ConstraintSolverEnv) ndeep m2 trace retry tpc1 tpc2 =
    trackErrors {
        let g = csenv.g
        let amap = csenv.amap
        let m = csenv.m
        match tpc1, tpc2 with
        | TyparConstraint.MayResolveMember(traitInfo1, _), TyparConstraint.MayResolveMember(traitInfo2, _)
            when TraitsAreRelated csenv retry traitInfo1 traitInfo2 ->
            let (TTrait(tys=tys1; objAndArgTys=argTys1; returnTyOpt=rty1)) = traitInfo1
            let (TTrait(tys=tys2; objAndArgTys=argTys2; returnTyOpt=rty2)) = traitInfo2
            if retry then
                match tys1, tys2 with
                | [ty1], [ty2] -> do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace ty1 ty2
                | [ty1], _ -> do! IterateD (SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace ty1) tys2
                | _, [ty2] -> do! IterateD (SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace ty2) tys1
                | _ -> ()
            do! Iterate2D (SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace) argTys1 argTys2
            let rty1 = GetFSharpViewOfReturnType g rty1
            let rty2 = GetFSharpViewOfReturnType g rty2
            do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace rty1 rty2

        | TyparConstraint.CoercesTo(ty1, _), TyparConstraint.CoercesTo(ty2, _) ->
            // Record at most one subtype constraint for each head type.
            // That is, we forbid constraints by both I<string> and I<int>.
            // This works because the types on the r.h.s. of subtype
            // constraints are head-types and so any further inferences are equational.
            let collect ty =
                let mutable res = []
                IterateEntireHierarchyOfType (fun x -> res <- x :: res) g amap m AllowMultiIntfInstantiations.No ty
                List.rev res
            let parents1 = collect ty1
            let parents2 = collect ty2
            for ty1Parent in parents1 do
                for ty2Parent in parents2 do
                    if HaveSameHeadType g ty1Parent ty2Parent then
                        do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace ty1Parent ty2Parent

        | TyparConstraint.IsEnum (underlyingTy1, _), TyparConstraint.IsEnum (underlyingTy2, m2) ->
            return! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace underlyingTy1 underlyingTy2

        | TyparConstraint.IsDelegate (argsTy1, retTy1, _), TyparConstraint.IsDelegate (argsTy2, retTy2, m2) ->
            do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace argsTy1 argsTy2
            return! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace retTy1 retTy2

        | TyparConstraint.SupportsComparison _, TyparConstraint.IsDelegate _
        | TyparConstraint.IsDelegate _, TyparConstraint.SupportsComparison _ ->
            return! ErrorD (Error(FSComp.SR.csComparisonDelegateConstraintInconsistent(), m))

        | TyparConstraint.IsNonNullableStruct _, TyparConstraint.IsReferenceType _
        | TyparConstraint.IsReferenceType _, TyparConstraint.IsNonNullableStruct _   ->
            return! ErrorD (Error(FSComp.SR.csStructConstraintInconsistent(), m))

        | TyparConstraint.SupportsNull _, TyparConstraint.NotSupportsNull _
        | TyparConstraint.NotSupportsNull _, TyparConstraint.SupportsNull _   ->
            return! ErrorD (Error(FSComp.SR.csNullNotNullConstraintInconsistent(), m))

        | TyparConstraint.SupportsNull _, TyparConstraint.IsNonNullableStruct _
        | TyparConstraint.IsNonNullableStruct _, TyparConstraint.SupportsNull _   ->
            ()
            //return! WarnD (Error(FSComp.SR.csNullStructConstraintInconsistent(), m))
        
        | TyparConstraint.IsUnmanaged _, TyparConstraint.IsReferenceType _
        | TyparConstraint.IsReferenceType _, TyparConstraint.IsUnmanaged _ ->
            return! ErrorD (Error(FSComp.SR.csUnmanagedConstraintInconsistent(), m))

        | TyparConstraint.SupportsComparison _, TyparConstraint.SupportsComparison _
        | TyparConstraint.SupportsEquality _, TyparConstraint.SupportsEquality _
        | TyparConstraint.SupportsNull _, TyparConstraint.SupportsNull _
        | TyparConstraint.NotSupportsNull _, TyparConstraint.NotSupportsNull _
        | TyparConstraint.IsNonNullableStruct _, TyparConstraint.IsNonNullableStruct _
        | TyparConstraint.IsUnmanaged _, TyparConstraint.IsUnmanaged _
        | TyparConstraint.IsReferenceType _, TyparConstraint.IsReferenceType _
        | TyparConstraint.RequiresDefaultConstructor _, TyparConstraint.RequiresDefaultConstructor _
        | TyparConstraint.SimpleChoice _, TyparConstraint.SimpleChoice _ ->
            ()
                
        | _ -> ()
    }

// See when one constraint implies another.
// 'a :> ty1  implies 'a :> 'ty2 if the head type name of ty2 (say T2) occursCheck anywhere in the hierarchy of ty1
// If it does occur, e.g. at instantiation T2<inst2>, then the check above will have enforced that
// T2<inst2> = ty2
and CheckConstraintImplication (csenv: ConstraintSolverEnv) tpc1 tpc2 =
    let g = csenv.g
    let aenv = csenv.EquivEnv
    let amap = csenv.amap
    let m = csenv.m
    match tpc1, tpc2 with
    | TyparConstraint.MayResolveMember(trait1, _), TyparConstraint.MayResolveMember(trait2, _) ->
        traitsAEquiv g aenv trait1 trait2

    | TyparConstraint.CoercesTo(ty1, _), TyparConstraint.CoercesTo(ty2, _) ->
        ExistsSameHeadTypeInHierarchy g amap m ty1 ty2

    | TyparConstraint.IsEnum(u1, _), TyparConstraint.IsEnum(u2, _) -> typeEquiv g u1 u2

    | TyparConstraint.IsDelegate(argsTy1, retyTy1, _), TyparConstraint.IsDelegate(argsTy2, retyTy2, _) ->
        typeEquiv g argsTy1 argsTy2 && typeEquiv g retyTy1 retyTy2

    | TyparConstraint.SupportsComparison _, TyparConstraint.SupportsComparison _
    | TyparConstraint.SupportsEquality _, TyparConstraint.SupportsEquality _
    // comparison implies equality
    | TyparConstraint.SupportsComparison _, TyparConstraint.SupportsEquality _
    // 'null' implies reference type ('not struct')
    | TyparConstraint.SupportsNull _, TyparConstraint.IsReferenceType _
    | TyparConstraint.SupportsNull _, TyparConstraint.SupportsNull _
    | TyparConstraint.NotSupportsNull _, TyparConstraint.NotSupportsNull _
    | TyparConstraint.IsNonNullableStruct _, TyparConstraint.IsNonNullableStruct _
    | TyparConstraint.IsUnmanaged _, TyparConstraint.IsUnmanaged _
    | TyparConstraint.AllowsRefStruct _, TyparConstraint.AllowsRefStruct _
    | TyparConstraint.IsReferenceType _, TyparConstraint.IsReferenceType _
    | TyparConstraint.RequiresDefaultConstructor _, TyparConstraint.RequiresDefaultConstructor _ -> true
    | TyparConstraint.SimpleChoice (tys1, _), TyparConstraint.SimpleChoice (tys2, _) -> ListSet.isSubsetOf (typeEquiv g) tys1 tys2
    | TyparConstraint.DefaultsTo (priority1, defaultTy1, _), TyparConstraint.DefaultsTo (priority2, defaultTy2, _) ->
            (priority1 = priority2) && typeEquiv g defaultTy1 defaultTy2
    | _ -> false
        
and CheckConstraintsImplication csenv existingConstraints newConstraint =
    existingConstraints |> List.exists (fun tpc2 -> CheckConstraintImplication csenv tpc2 newConstraint)

and SubtypeConstraintImplied g existingConstraints newCoarceToTy =
    if g.checkNullness then
        let canBeNull t = (nullnessOfTy g t).Evaluate() = NullnessInfo.WithNull
        let newTyIsWithoutNull = canBeNull newCoarceToTy |> not
        let typeCoversNewConstraint existingTy = 
            typeEquiv g existingTy newCoarceToTy
            && not (newTyIsWithoutNull && canBeNull existingTy)   // :> T?  cannot imply :>T, since non-nullable is a stricter constraint.

        existingConstraints 
        |> List.exists (function | TyparConstraint.CoercesTo(ty2,_) when typeCoversNewConstraint ty2 -> true | _ -> false)
    else
        false

// Ensure constraint conforms with existing constraints
// NOTE: QUADRATIC
and EnforceConstraintSetConsistency csenv ndeep m2 trace retry allCxs i cxs =
    match cxs with
    | [] ->  CompleteD
    | cx :: rest ->
        trackErrors {
            do! IterateIdxD (fun j cx2 -> if i = j then CompleteD else EnforceConstraintConsistency csenv ndeep m2 trace retry cx cx2) allCxs
            return! EnforceConstraintSetConsistency csenv ndeep m2 trace retry allCxs (i+1) rest
        }

// Eliminate any constraints where one constraint implies another
// Keep constraints in the left-to-right form according to the order they are asserted.
// NOTE: QUADRATIC
and EliminateRedundantConstraints csenv cxs acc =
    match cxs with
    | [] -> acc
    | cx :: rest ->
        let acc =
            if List.exists (fun cx2 -> CheckConstraintImplication csenv cx2 cx) acc then acc
            else (cx :: acc)
        EliminateRedundantConstraints csenv rest acc

/// Record a constraint on an inference type variable.
and AddConstraint (csenv: ConstraintSolverEnv) ndeep m2 trace tp newConstraint  =
    let denv = csenv.DisplayEnv
    let m = csenv.m
    let g = csenv.g

    let existingConstraints = tp.Constraints

    let allCxs = newConstraint :: List.rev existingConstraints
    trackErrors {
        do! EnforceConstraintSetConsistency csenv ndeep m2 trace false allCxs 0 allCxs
    
        let mutable impliedByExistingConstraints = CheckConstraintsImplication csenv existingConstraints newConstraint

        // When InterfacesWithAbstractStaticMembers enabled, retry constraint consistency and implication when one of the constraints is known to have
        // a single support type, and the other has two support types.
        //    (T1 : static member Foo: int)
        // and the constraint we're adding is this:
        //    ((T2 or ?inf) : static member Foo: int)
        //
        // Then the only logical solution is ?inf = T1 = T2.  So just enforce this and try again.
        if
            not impliedByExistingConstraints &&
            (IsRigid csenv tp || tp.Rigidity.WarnIfMissingConstraint) &&
            g.langVersion.SupportsFeature LanguageFeature.InterfacesWithAbstractStaticMembers
        then
            do! EnforceConstraintSetConsistency csenv ndeep m2 trace true allCxs 0 allCxs
            impliedByExistingConstraints <- CheckConstraintsImplication csenv existingConstraints newConstraint

        if impliedByExistingConstraints then ()
        // "Default" constraints propagate softly and can be omitted from explicit declarations of type parameters
        elif (match tp.Rigidity, newConstraint with 
              | (TyparRigidity.Rigid | TyparRigidity.WillBeRigid), TyparConstraint.DefaultsTo _ -> true
              | _ -> false) then 
            ()
        elif IsRigid csenv tp then
            if not impliedByExistingConstraints then
                return! ErrorD (ConstraintSolverMissingConstraint(denv, tp, newConstraint, m, m2))
        else
            // It is important that we give a warning if a constraint is missing from a 
            // will-be-made-rigid type variable. This is because the existence of these warnings
            // is relevant to the overload resolution rules (see 'candidateWarnCount' in the overload resolution
            // implementation).
            if tp.Rigidity.WarnIfMissingConstraint then
                do! WarnD (ConstraintSolverMissingConstraint(denv, tp, newConstraint, m, m2))

            let newConstraints = EliminateRedundantConstraints csenv allCxs []

            // Write the constraint into the type variable 
            // Record a entry in the undo trace if one is provided 
            let orig = tp.Constraints
            trace.Exec (fun () -> tp.SetConstraints newConstraints) (fun () -> tp.SetConstraints orig)
            ()
    }

// preferConstraint: if the type is a type variable with an uncertain nullness, then
// this indicates whether we prefer to add a nullness constraint to the type variable itself,
// or whether we prefer to solve the nullness annotation.
//
// This is relevant for code like this:
//
//    let isNull (value : 'T when 'T : null) = 
//        match box value with 
//        | null -> true 
//        | _ -> false
//
//    let checkNonNull argName arg =
//        if isNull arg then
//            failwith (argName + " is null")
//
// Here the `'T: null` constraint is propagated by inference to checkNonNull. Which of these two types do we expect?
//
//    val checkNonNull1: argName: string -> arg: 'T -> unit when 'T: null
//
//    val checkNonNull2: argName: string -> arg: 'T | null -> unit
//
// When null checking is fully enabled, we prefer the latter. We can't always prefer it because it is a breaking change.
//
// Likewise consider
//
//    let x = null
//
// What's the generalized type?
//    val x: 'a when 'a: null
//    val x: 'a | null when 'a: not null
//
// When null checking is fully enabled, we prefer the latter. We can't always prefer it because it is a breaking change.
and SolveTypeUseSupportsNull (csenv: ConstraintSolverEnv) ndeep m2 trace ty = 
    trackErrors {
        let g = csenv.g
        let m = csenv.m
        let denv = csenv.DisplayEnv
        if g.langFeatureNullness then 
            if TypeNullIsExtraValueNew g m ty then 
                ()
            elif isNullableTy g ty then
                return! ErrorD (ConstraintSolverError(FSComp.SR.csNullableTypeDoesNotHaveNull(NicePrint.minimalStringOfType denv ty), m, m2))
            else
                match tryDestTyparTy g ty with
                | ValueSome tp ->                    
                    let nullness = nullnessOfTy g ty
                    match nullness.TryEvaluate() with
                    // NULLNESS TODO: This rule means turning on checkNullness changes type inference results for the cases
                    // mentioned in the comment above. THat's OK but needs to be documented in the RFC.
                    | ValueNone when not g.checkNullness ->
                        return! AddConstraint csenv ndeep m2 trace tp (TyparConstraint.SupportsNull m)                    
                    | ValueSome NullnessInfo.WithoutNull ->                      
                        return! AddConstraint csenv ndeep m2 trace tp (TyparConstraint.SupportsNull m)
                    | _ ->
                        if not (tp |> HasConstraint _.IsIsReferenceType) then
                            do! AddConstraint csenv ndeep m2 trace tp (TyparConstraint.IsReferenceType m)
                        return! SolveNullnessSupportsNull csenv ndeep m2 trace ty nullness
                | _ ->
                    let nullness = nullnessOfTy g ty
                    do! SolveNullnessSupportsNull csenv ndeep m2 trace ty nullness

                    // If checkNullness is off give the same errors as F# 4.5
                    if not g.checkNullness && not (TypeNullIsExtraValue g m ty) then
                        return! ErrorD (ConstraintSolverError(FSComp.SR.csTypeDoesNotHaveNull(NicePrint.minimalStringOfType denv ty), m, m2))
        else
            if TypeNullIsExtraValue g m ty then
                ()
            elif isNullableTy g ty then
                return! ErrorD (ConstraintSolverError(FSComp.SR.csNullableTypeDoesNotHaveNull(NicePrint.minimalStringOfType denv ty), m, m2))
            else
                match tryDestTyparTy g ty with
                | ValueSome tp ->
                    do! AddConstraint csenv ndeep m2 trace tp (TyparConstraint.SupportsNull m)
                | ValueNone ->
                    return! ErrorD (ConstraintSolverError(FSComp.SR.csTypeDoesNotHaveNull(NicePrint.minimalStringOfType denv ty), m, m2))
        }

and SolveNullnessSupportsNull (csenv: ConstraintSolverEnv) ndeep m2 (trace: OptionalTrace) ty nullness =
    trackErrors {
        let g = csenv.g
        let m = csenv.m
        let denv = csenv.DisplayEnv
        match nullness with
        | Nullness.Variable nv ->
            if nv.IsSolved then
                do! SolveNullnessSupportsNull csenv ndeep m2 trace ty nv.Solution
            else
                trace.Exec (fun () -> nv.Set KnownWithNull) (fun () -> nv.Unset())
        | Nullness.Known n1 -> 
            match n1 with 
            | NullnessInfo.AmbivalentToNull -> ()
            | NullnessInfo.WithNull -> ()
            | NullnessInfo.WithoutNull ->   
                if g.checkNullness then
                    // If a type would allow null in older rules of F#, we can just emit a warning.
                    // In the opposite case, we keep this as an error to avoid generating incorrect code (e.g. assigning null to an int)
                    if (TypeNullIsExtraValue g m ty) then
                        return! WarnD(ConstraintSolverNullnessWarningWithType(denv, ty, n1, m, m2))
                    else
                        return! ErrorD (ConstraintSolverError(FSComp.SR.csTypeDoesNotHaveNull(NicePrint.minimalStringOfType denv ty), m, m2))
    }

and SolveTypeUseNotSupportsNull (csenv: ConstraintSolverEnv) ndeep m2 trace ty =
    trackErrors {
        let g = csenv.g
        let m = csenv.m
        let denv = csenv.DisplayEnv

        if TypeNullIsTrueValue g ty then 
            // We can only give warnings here as F# 5.0 introduces these constraints into existing
            // code via Option.ofObj and Option.toObj
            do! WarnD (ConstraintSolverNullnessWarning(FSComp.SR.csTypeHasNullAsTrueValue(NicePrint.minimalStringOfType denv ty), m, m2))
        elif TypeNullIsExtraValueNew g m ty then 
            if g.checkNullness then
                do! WarnD (ConstraintSolverNullnessWarning(FSComp.SR.csTypeHasNullAsExtraValue(NicePrint.minimalStringOfTypeWithNullness denv ty), m, m2))
        else
            match tryDestTyparTy g ty with
            | ValueSome tp ->
                do! AddConstraint csenv ndeep m2 trace tp (TyparConstraint.NotSupportsNull m)
            | ValueNone ->
                let nullness = nullnessOfTy g ty
                do! SolveNullnessNotSupportsNull csenv ndeep m2 trace ty nullness
    }

and SolveNullnessNotSupportsNull (csenv: ConstraintSolverEnv) ndeep m2 (trace: OptionalTrace) ty nullness =
    trackErrors {
        let g = csenv.g
        let m = csenv.m
        let denv = csenv.DisplayEnv
        match nullness with
        | Nullness.Variable nv ->
            if nv.IsSolved then
                do! SolveNullnessNotSupportsNull csenv ndeep m2 trace ty nv.Solution
            else
                trace.Exec (fun () -> nv.Set KnownWithoutNull) (fun () -> nv.Unset())
        | Nullness.Known n1 -> 
            match n1 with 
            | NullnessInfo.AmbivalentToNull -> ()
            | NullnessInfo.WithoutNull -> ()
            | NullnessInfo.WithNull -> 
                if g.checkNullness && TypeNullIsExtraValueNew g m ty then
                    return! WarnD(ConstraintSolverNullnessWarning(FSComp.SR.csTypeHasNullAsExtraValue(NicePrint.minimalStringOfTypeWithNullness denv ty), m, m2))
    }

and SolveTypeCanCarryNullness (csenv: ConstraintSolverEnv)  ty nullness =
    trackErrors {
        let g = csenv.g
        let m = csenv.m
        let strippedTy = stripTyEqnsA g true ty
        match tryAddNullnessToTy nullness strippedTy with
        | Some _ -> 
            if isTyparTy g strippedTy && not (IsReferenceTyparTy g strippedTy) then
                return! AddConstraint csenv 0 m NoTrace (destTyparTy g strippedTy) (TyparConstraint.IsReferenceType m)
        | None -> 
            let tyString = NicePrint.minimalStringOfType csenv.DisplayEnv strippedTy
            return! ErrorD(Error(FSComp.SR.tcTypeDoesNotHaveAnyNull(tyString), m))
    }

and SolveTypeSupportsComparison (csenv: ConstraintSolverEnv) ndeep m2 trace ty =
    let g = csenv.g
    let m = csenv.m
    let amap = csenv.amap
    let denv = csenv.DisplayEnv
    match tryDestTyparTy g ty with
    | ValueSome destTypar ->
        AddConstraint csenv ndeep m2 trace destTypar (TyparConstraint.SupportsComparison m)
    | ValueNone ->
        // Check it isn't ruled out by the user
        match tryTcrefOfAppTy g ty with 
        | ValueSome tcref when HasFSharpAttribute g g.attrib_NoComparisonAttribute tcref.Attribs ->
            ErrorD (ConstraintSolverError(FSComp.SR.csTypeDoesNotSupportComparison1(NicePrint.minimalStringOfType denv ty), m, m2))
        | _ ->
            match ty with 
            | SpecialComparableHeadType g tinst ->
                 IterateD (SolveTypeSupportsComparison (csenv: ConstraintSolverEnv) ndeep m2 trace) tinst
            | _ -> 
               // Check the basic requirement - IComparable or IStructuralComparable or assumed
               if ExistsSameHeadTypeInHierarchy g amap m2 ty g.mk_IComparable_ty  ||
                  ExistsSameHeadTypeInHierarchy g amap m2 ty g.mk_IStructuralComparable_ty
               then 
                   // The type is comparable because it implements IComparable
                    match ty with
                    | AppTy g (tcref, tinst) ->
                        // Check the (possibly inferred) structural dependencies
                        (tinst, tcref.TyparsNoRange) ||> Iterate2D (fun ty tp -> 
                            if tp.ComparisonConditionalOn then 
                                SolveTypeSupportsComparison (csenv: ConstraintSolverEnv) ndeep m2 trace ty 
                            else 
                                CompleteD) 
                    | _ ->
                        CompleteD

               // Give a good error for structural types excluded from the comparison relation because of their fields
               elif (isAppTy g ty && 
                     let tcref = tcrefOfAppTy g ty 
                     AugmentTypeDefinitions.TyconIsCandidateForAugmentationWithCompare g tcref.Deref && 
                     Option.isNone tcref.GeneratedCompareToWithComparerValues) then
 
                   ErrorD (ConstraintSolverError(FSComp.SR.csTypeDoesNotSupportComparison3(NicePrint.minimalStringOfType denv ty), m, m2))

               else 
                   ErrorD (ConstraintSolverError(FSComp.SR.csTypeDoesNotSupportComparison2(NicePrint.minimalStringOfType denv ty), m, m2))

and SolveTypeSupportsEquality (csenv: ConstraintSolverEnv) ndeep m2 trace ty =
    let g = csenv.g
    let m = csenv.m
    let denv = csenv.DisplayEnv
    match tryDestTyparTy g ty with
    | ValueSome destTypar ->
        AddConstraint csenv ndeep m2 trace destTypar (TyparConstraint.SupportsEquality m)
    | _ ->
        match tryTcrefOfAppTy g ty with 
        | ValueSome tcref when HasFSharpAttribute g g.attrib_NoEqualityAttribute tcref.Attribs ->
            ErrorD (ConstraintSolverError(FSComp.SR.csTypeDoesNotSupportEquality1(NicePrint.minimalStringOfType denv ty), m, m2))
        | _ ->
            match ty with 
            | SpecialEquatableHeadType g tinst -> 
                tinst |> IterateD (SolveTypeSupportsEquality (csenv: ConstraintSolverEnv) ndeep m2 trace)
            | SpecialNotEquatableHeadType g _ -> 
                ErrorD (ConstraintSolverError(FSComp.SR.csTypeDoesNotSupportEquality2(NicePrint.minimalStringOfType denv ty), m, m2))
            | _ -> 
               // The type is equatable because it has Object.Equals(...)
               match ty with
               | AppTy g (tcref, tinst) ->
                   // Give a good error for structural types excluded from the equality relation because of their fields
                   if AugmentTypeDefinitions.TyconIsCandidateForAugmentationWithEquals g tcref.Deref && 
                       Option.isNone tcref.GeneratedHashAndEqualsWithComparerValues 
                   then
                       ErrorD (ConstraintSolverError(FSComp.SR.csTypeDoesNotSupportEquality3(NicePrint.minimalStringOfType denv ty), m, m2))
                   else
                       // Check the (possibly inferred) structural dependencies
                       (tinst, tcref.TyparsNoRange) ||> Iterate2D (fun ty tp -> 
                           if tp.EqualityConditionalOn then 
                               SolveTypeSupportsEquality csenv ndeep m2 trace ty
                           else 
                               CompleteD) 
               | _ ->
                   CompleteD
           
and SolveTypeIsEnum (csenv: ConstraintSolverEnv) ndeep m2 trace ty underlying =
    let g = csenv.g
    let m = csenv.m
    let denv = csenv.DisplayEnv
    match tryDestTyparTy g ty with
    | ValueSome destTypar ->
        AddConstraint csenv ndeep m2 trace destTypar (TyparConstraint.IsEnum(underlying, m))
    | _ ->
        if isEnumTy g ty then 
            SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace underlying (underlyingTypeOfEnumTy g ty) 
        else 
            ErrorD (ConstraintSolverError(FSComp.SR.csTypeIsNotEnumType(NicePrint.minimalStringOfType denv ty), m, m2))

and SolveTypeIsDelegate (csenv: ConstraintSolverEnv) ndeep m2 trace ty aty bty =
    let g = csenv.g
    let m = csenv.m
    let denv = csenv.DisplayEnv
    match tryDestTyparTy g ty with
    | ValueSome destTypar ->
        AddConstraint csenv ndeep m2 trace destTypar (TyparConstraint.IsDelegate(aty, bty, m))
    | _ ->
        if isDelegateTy g ty then 
            match TryDestStandardDelegateType csenv.InfoReader m AccessibleFromSomewhere ty with 
            | Some (tupledArgTy, retTy) ->
                trackErrors {
                    do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace aty tupledArgTy 
                    do! SolveTypeEqualsTypeKeepAbbrevs csenv ndeep m2 trace bty retTy 
                }
            | None ->
                ErrorD (ConstraintSolverError(FSComp.SR.csTypeHasNonStandardDelegateType(NicePrint.minimalStringOfType denv ty), m, m2))
        else 
            ErrorD (ConstraintSolverError(FSComp.SR.csTypeIsNotDelegateType(NicePrint.minimalStringOfType denv ty), m, m2))
    
and SolveTypeIsNonNullableValueType (csenv: ConstraintSolverEnv) ndeep m2 trace ty =
    let g = csenv.g
    let m = csenv.m
    let denv = csenv.DisplayEnv
    match tryDestTyparTy g ty with
    | ValueSome destTypar ->
        AddConstraint csenv ndeep m2 trace destTypar (TyparConstraint.IsNonNullableStruct m)
    | _ ->
        let underlyingTy = stripTyEqnsAndMeasureEqns g ty
        if isStructTy g underlyingTy then
            if isNullableTy g underlyingTy then
                ErrorD (ConstraintSolverError(FSComp.SR.csTypeParameterCannotBeNullable(), m, m))
            else
                CompleteD
        else
            ErrorD (ConstraintSolverError(FSComp.SR.csGenericConstructRequiresStructType(NicePrint.minimalStringOfType denv ty), m, m2))

and SolveTypeIsUnmanaged (csenv: ConstraintSolverEnv) ndeep m2 trace ty =
    let g = csenv.g
    let m = csenv.m
    let denv = csenv.DisplayEnv
    match tryDestTyparTy g ty with
    | ValueSome destTypar ->
        AddConstraint csenv ndeep m2 trace destTypar (TyparConstraint.IsUnmanaged m)
    | _ ->
        if isStructAnonRecdTy g ty then
            destStructAnonRecdTy g ty |> IterateD (SolveTypeIsUnmanaged csenv (ndeep + 1) m2 trace)
        else if isStructTupleTy g ty then
            destStructTupleTy g ty |> IterateD (SolveTypeIsUnmanaged csenv (ndeep + 1) m2 trace)
        else if isStructUnionTy g ty then
            let tcref = tryTcrefOfAppTy g ty |> ValueOption.get
            let tinst = mkInstForAppTy g ty
            
            tcref.UnionCasesAsRefList            
            |> List.collect (actualTysOfUnionCaseFields tinst)
            |> IterateD (SolveTypeIsUnmanaged csenv (ndeep + 1) m2 trace)
        else
            if isUnmanagedTy g ty then
                CompleteD
            else
                ErrorD (ConstraintSolverError(FSComp.SR.csGenericConstructRequiresUnmanagedType(NicePrint.minimalStringOfType denv ty), m, m2))

and SolveTypeChoice (csenv: ConstraintSolverEnv) ndeep m2 trace ty choiceTys =
    trackErrors {
        let g = csenv.g
        let m = csenv.m
        let denv = csenv.DisplayEnv
        match tryDestTyparTy g ty with
        | ValueSome destTypar ->
            // SolveTypStaticReq is applied here if IWSAMs are supported
            if g.langVersion.SupportsFeature LanguageFeature.InterfacesWithAbstractStaticMembers then
                do! SolveTypStaticReq csenv trace TyparStaticReq.HeadType ty

            return! AddConstraint csenv ndeep m2 trace destTypar (TyparConstraint.SimpleChoice(choiceTys, m))
        | _ ->
            if not (choiceTys |> List.exists (typeEquivAux Erasure.EraseMeasures g ty)) then
                let tyString = NicePrint.minimalStringOfType denv ty
                let tysString = choiceTys |> List.map (NicePrint.prettyStringOfTy denv) |> String.concat ","
                return! ErrorD (ConstraintSolverError(FSComp.SR.csTypeNotCompatibleBecauseOfPrintf(tyString, tysString), m, m2))
    }

and SolveTypeIsReferenceType (csenv: ConstraintSolverEnv) ndeep m2 trace ty =
    let g = csenv.g
    let m = csenv.m
    let denv = csenv.DisplayEnv
    match tryDestTyparTy g ty with
    | ValueSome destTypar ->
        AddConstraint csenv ndeep m2 trace destTypar (TyparConstraint.IsReferenceType m)
    | _ ->
        if isRefTy g ty then CompleteD
        else ErrorD (ConstraintSolverError(FSComp.SR.csGenericConstructRequiresReferenceSemantics(NicePrint.minimalStringOfType denv ty), m, m))

and SolveTypeRequiresDefaultConstructor (csenv: ConstraintSolverEnv) ndeep m2 trace origTy =
    let g = csenv.g
    let amap = csenv.amap
    let m = csenv.m
    let denv = csenv.DisplayEnv
    let ty = stripTyEqnsAndMeasureEqns g origTy
    match tryDestTyparTy g ty with
    | ValueSome tp ->
        AddConstraint csenv ndeep m2 trace tp (TyparConstraint.RequiresDefaultConstructor m)
    | _ ->
        if isStructTy g ty && (isStructTupleTy g ty || isStructAnonRecdTy g ty || TypeHasDefaultValue g m ty) then
            if isStructTupleTy g ty then 
                destStructTupleTy g ty |> IterateD (SolveTypeRequiresDefaultValue csenv ndeep m trace)
            elif isStructAnonRecdTy g ty then 
                match tryDestAnonRecdTy g ty with
                | ValueNone -> CompleteD
                | ValueSome (_, ptys) -> ptys |> IterateD (SolveTypeRequiresDefaultValue csenv ndeep m trace)
            elif TypeHasDefaultValue g m ty then
                CompleteD
            else
                ErrorD (ConstraintSolverError(FSComp.SR.csGenericConstructRequiresPublicDefaultConstructor(NicePrint.minimalStringOfType denv origTy), m, m2))
        else
            if GetIntrinsicConstructorInfosOfType csenv.InfoReader m ty 
               |> List.exists (fun x -> x.IsNullary && IsMethInfoAccessible amap m AccessibleFromEverywhere x)
            then 
                match tryTcrefOfAppTy g ty with
                | ValueSome tcref when HasFSharpAttribute g g.attrib_AbstractClassAttribute tcref.Attribs ->
                    ErrorD (ConstraintSolverError(FSComp.SR.csGenericConstructRequiresNonAbstract(NicePrint.minimalStringOfType denv origTy), m, m2))
                | _ ->
                    CompleteD
            else
                match tryTcrefOfAppTy g ty with
                | ValueSome tcref when
                    tcref.PreEstablishedHasDefaultConstructor || 
                    // F# 3.1 feature: records with CLIMutable attribute should satisfy 'default constructor' constraint
                    (tcref.IsRecordTycon && HasFSharpAttribute g g.attrib_CLIMutableAttribute tcref.Attribs) ->
                    CompleteD
                | _ -> 
                    ErrorD (ConstraintSolverError(FSComp.SR.csGenericConstructRequiresPublicDefaultConstructor(NicePrint.minimalStringOfType denv origTy), m, m2))

// Note, this constraint arises structurally when processing the element types of struct tuples and struct anonymous records.
//
// In the case of type variables, it requires that the type variable already have been pre-established to be either a (non-nullable) struct
// or a reference type.
and SolveTypeRequiresDefaultValue (csenv: ConstraintSolverEnv) ndeep m2 trace origTy =
    let g = csenv.g
    let m = csenv.m
    let ty = stripTyEqnsAndMeasureEqns g origTy

    if isTyparTy g ty then
        if IsNonNullableStructTyparTy g ty then
            SolveTypeRequiresDefaultConstructor csenv ndeep m2 trace ty 
        elif IsReferenceTyparTy g ty then
            SolveTypeUseSupportsNull csenv ndeep m2 trace ty
        else
            ErrorD (ConstraintSolverError(FSComp.SR.csGenericConstructRequiresStructOrReferenceConstraint(), m, m2))
    else
        if isStructTy g ty then
             SolveTypeRequiresDefaultConstructor csenv ndeep m2 trace ty 
        else
             SolveTypeUseSupportsNull csenv ndeep m2 trace ty

// Parameterized compatibility relation between member signatures.  The real work
// is done by "equateTypes" and "subsumeTypes" and "subsumeArg"
and CanMemberSigsMatchUpToCheck 
      (csenv: ConstraintSolverEnv) 
      // are we allowed to supply optional and/or "param" arguments?
      permitOptArgs 
      // always check the return type?
      alwaysCheckReturn 
      // Used to equate the formal method instantiation with the actual method instantiation
      // for a generic method, and the return types
      (unifyTypes: TType -> TType -> OperationResult<TypeDirectedConversionUsed>)
      // Used to compare the "obj" type 
      (subsumeTypes: TType -> TType -> OperationResult<TypeDirectedConversionUsed>)
      // Used to convert the "return" for MustConvertTo
      (subsumeOrConvertTypes: bool -> TType -> TType -> OperationResult<TypeDirectedConversionUsed>)
      // Used to convert the arguments
      (subsumeOrConvertArg: CalledArg -> CallerArg<_> -> OperationResult<TypeDirectedConversionUsed>)
      (reqdRetTyOpt: OverallTy option) 
      (calledMeth: CalledMeth<_>): OperationResult<TypeDirectedConversionUsed> =
        trackErrors {
            let g    = csenv.g
            let amap = csenv.amap
            let m    = csenv.m
    
            let minfo = calledMeth.Method
            let minst = calledMeth.CalledTyArgs
            let uminst = calledMeth.CallerTyArgs
            let callerObjArgTys = calledMeth.CallerObjArgTys
            let assignedItemSetters = calledMeth.AssignedItemSetters
            let unnamedCalledOptArgs = calledMeth.UnnamedCalledOptArgs
            let unnamedCalledOutArgs = calledMeth.UnnamedCalledOutArgs

            // First equate the method instantiation (if any) with the method type parameters 
            if minst.Length <> uminst.Length then 
                return! ErrorD(Error(FSComp.SR.csTypeInstantiationLengthMismatch(), m))
            else
                let! usesTDC1 = MapCombineTDC2D unifyTypes minst uminst
                let! usesTDC2 =
                    if not (permitOptArgs || isNil unnamedCalledOptArgs) then 
                        ErrorD(Error(FSComp.SR.csOptionalArgumentNotPermittedHere(), m)) 
                    else
                        let calledObjArgTys = calledMeth.CalledObjArgTys(m)

                        // Check all the argument types. 

                        if calledObjArgTys.Length <> callerObjArgTys.Length then 
                            if calledObjArgTys.Length <> 0 then
                                ErrorD(Error (FSComp.SR.csMemberIsNotStatic(minfo.LogicalName), m))
                            else
                                ErrorD(Error (FSComp.SR.csMemberIsNotInstance(minfo.LogicalName), m))
                        else
                            // The object types must be non-null
                            let nonNullCalledObjArgTys = 
                                if not calledMeth.Method.IsExtensionMember then
                                    calledObjArgTys |> List.map (replaceNullnessOfTy g.knownWithoutNull)
                                else
                                    calledObjArgTys
                            MapCombineTDC2D subsumeTypes nonNullCalledObjArgTys callerObjArgTys

                let! usesTDC3 =
                    calledMeth.ArgSets |> MapCombineTDCD (fun argSet ->
                        if argSet.UnnamedCalledArgs.Length <> argSet.UnnamedCallerArgs.Length then 
                            ErrorD(Error(FSComp.SR.csArgumentLengthMismatch(), m))
                        else
                            MapCombineTDC2D subsumeOrConvertArg argSet.UnnamedCalledArgs argSet.UnnamedCallerArgs
                    )

                let! usesTDC4 =
                    match calledMeth.ParamArrayCalledArgOpt with
                    | Some calledArg ->
                        if isArray1DTy g calledArg.CalledArgumentType then 
                            let paramArrayElemTy = destArrayTy g calledArg.CalledArgumentType
                            let reflArgInfo = calledArg.ReflArgInfo // propagate the reflected-arg info to each param array argument
                            match calledMeth.ParamArrayCallerArgs with
                            | Some args ->
                                args |> MapCombineTDCD (fun callerArg -> 
                                    subsumeOrConvertArg (CalledArg((0, 0), false, NotOptional, NoCallerInfo, false, false, None, reflArgInfo, paramArrayElemTy)) callerArg
                                )


                            | _ -> ResultD TypeDirectedConversionUsed.No
                        else
                            ResultD TypeDirectedConversionUsed.No
                    | _ -> ResultD TypeDirectedConversionUsed.No

                let! usesTDC5 =
                    calledMeth.ArgSets |> MapCombineTDCD (fun argSet -> 
                        argSet.AssignedNamedArgs |> MapCombineTDCD (fun arg -> 
                            subsumeOrConvertArg arg.CalledArg arg.CallerArg
                        )
                    )

                let! usesTDC6 =
                  assignedItemSetters |> MapCombineTDCD (fun (AssignedItemSetter(_, item, caller)) ->
                    let name, calledArgTy = 
                        match item with
                        | AssignedPropSetter(_, _, pminfo, pminst) ->
                            let calledArgTy = List.head (List.head (pminfo.GetParamTypes(amap, m, pminst)))
                            pminfo.LogicalName, calledArgTy

                        | AssignedILFieldSetter(finfo) ->
                            let calledArgTy = finfo.FieldType(amap, m)
                            finfo.FieldName, calledArgTy
                
                        | AssignedRecdFieldSetter(rfinfo) ->
                            let calledArgTy = rfinfo.FieldType
                            rfinfo.LogicalName, calledArgTy
            
                    subsumeOrConvertArg (CalledArg((-1, 0), false, NotOptional, NoCallerInfo, false, false, Some (mkSynId m name), ReflectedArgInfo.None, calledArgTy)) caller
                  )
                // - Always take the return type into account for resolving overloading of
                //      -- op_Explicit, op_Implicit
                //      -- methods using tupling of unfilled out args
                // - Never take into account return type information for constructors 
                let! usesTDC7 =
                    match reqdRetTyOpt with
                    | Some _  when ( (* minfo.IsConstructor || *) not alwaysCheckReturn && isNil unnamedCalledOutArgs) ->
                        ResultD TypeDirectedConversionUsed.No
                    | Some (MustConvertTo(isMethodArg, reqdTy)) when g.langVersion.SupportsFeature LanguageFeature.AdditionalTypeDirectedConversions ->
                        let methodRetTy = calledMeth.CalledReturnTypeAfterOutArgTupling
                        subsumeOrConvertTypes isMethodArg reqdTy methodRetTy
                    | Some reqdRetTy ->
                        let methodRetTy = calledMeth.CalledReturnTypeAfterOutArgTupling
                        unifyTypes reqdRetTy.Commit methodRetTy
                    | _ ->
                        ResultD TypeDirectedConversionUsed.No
                return Array.reduce TypeDirectedConversionUsed.Combine [| usesTDC1; usesTDC2; usesTDC3; usesTDC4; usesTDC5; usesTDC6; usesTDC7 |]
        }

// Wrap an ErrorsFromAddingSubsumptionConstraint error around any failure 
// to allow us to report the outer types involved in the constraint 
//
// ty1: expected
// ty2: actual
//
// "ty2 casts to ty1"
// "a value of type ty2 can be used where a value of type ty1 is expected"
and AddWrappedContextualSubsumptionReport (csenv: ConstraintSolverEnv) ndeep m cxsln ty1 ty2 res wrapper =
    match csenv.eContextInfo with
    | ContextInfo.RuntimeTypeTest isOperator ->
        // test if we can cast other way around
        let results = 
            CollectThenUndo (fun newTrace ->
                SolveTypeSubsumesTypeKeepAbbrevs csenv ndeep m (WithTrace newTrace) cxsln ty2 ty1) 
        match results with 
        | OkResult _ -> ErrorD (wrapper (ErrorsFromAddingSubsumptionConstraint(csenv.g, csenv.DisplayEnv, ty1, ty2, res, ContextInfo.DowncastUsedInsteadOfUpcast isOperator, m)))
        | _ -> ErrorD (wrapper (ErrorsFromAddingSubsumptionConstraint(csenv.g, csenv.DisplayEnv, ty1, ty2, res, ContextInfo.NoContext, m)))
    | _ -> ErrorD (wrapper (ErrorsFromAddingSubsumptionConstraint(csenv.g, csenv.DisplayEnv, ty1, ty2, res, csenv.eContextInfo, m)))

/// Assert a subtype constraint
and SolveTypeSubsumesTypeWithWrappedContextualReport (csenv: ConstraintSolverEnv) ndeep m trace cxsln origTy1 ty1 ty2 wrapper =
    // Due to the legacy of the change https://github.com/dotnet/fsharp/pull/1650, 
    // when doing nested, speculative overload resolution, we ignore failed member constraints and continue.  The
    // constraint is not recorded for later solution.
    if csenv.IsSpeculativeForMethodOverloading then
        IgnoreFailedMemberConstraintResolution
            (fun () -> SolveTypeSubsumesTypeKeepAbbrevs csenv ndeep m trace cxsln ty1 ty2)
            (fun res -> AddWrappedContextualSubsumptionReport csenv ndeep m cxsln (defaultArg origTy1 ty1) ty2 res wrapper)
    else
        PostponeOnFailedMemberConstraintResolution csenv trace
            (fun csenv -> SolveTypeSubsumesTypeKeepAbbrevs csenv ndeep m trace cxsln ty1 ty2)
            (fun res -> AddWrappedContextualSubsumptionReport csenv ndeep m cxsln (defaultArg origTy1 ty1) ty2 res wrapper)
       
and SolveTypeSubsumesTypeWithReport (csenv: ConstraintSolverEnv) ndeep m trace cxsln origTy1 ty1 ty2 =
    SolveTypeSubsumesTypeWithWrappedContextualReport csenv ndeep m trace cxsln origTy1 ty1 ty2 id

and SolveTypeEqualsTypeWithReport (csenv: ConstraintSolverEnv) ndeep m trace cxsln expectedTy actualTy =
    TryD
        (fun () -> SolveTypeEqualsTypeKeepAbbrevsWithCxsln csenv ndeep m trace cxsln expectedTy actualTy)
        (function
        | AbortForFailedMemberConstraintResolution as err -> ErrorD err
        | res -> ErrorD (ErrorFromAddingTypeEquation(csenv.g, csenv.DisplayEnv, expectedTy, actualTy, res, m)))
  
and ArgsMustSubsumeOrConvert 
        (csenv: ConstraintSolverEnv)
        ad
        ndeep
        trace
        cxsln
        isConstraint
        enforceNullableOptionalsKnownTypes // use known types from nullable optional args?
        (calledArg: CalledArg) 
        (callerArg: CallerArg<'T>) =
    trackErrors {
        let g = csenv.g
        let m = callerArg.Range
        let callerTy = callerArg.CallerArgumentType
        let calledArgTy, usesTDC, eqn = AdjustCalledArgType csenv.InfoReader ad isConstraint enforceNullableOptionalsKnownTypes calledArg callerArg

        match eqn with 
        | Some (ty1, ty2, msg) ->
            do! SolveTypeEqualsTypeWithReport csenv ndeep m trace cxsln ty1 ty2
            msg csenv.DisplayEnv
        | None -> ()
        
        match usesTDC with 
        | TypeDirectedConversionUsed.Yes(warn, _, _) -> do! WarnD(warn csenv.DisplayEnv)
        | TypeDirectedConversionUsed.No -> ()
        do! SolveTypeSubsumesTypeWithReport csenv ndeep m trace cxsln (Some calledArg.CalledArgumentType) calledArgTy callerTy
        if g.langVersion.SupportsFeature(LanguageFeature.WarnWhenUnitPassedToObjArg) && isUnitTy g callerTy && isObjTyAnyNullness g calledArgTy then
            do! WarnD(Error(FSComp.SR.tcUnitToObjSubsumption(), m))
        if calledArg.IsParamArray && isArray1DTy g calledArgTy && not (isArray1DTy g callerTy) then 
            return! ErrorD(Error(FSComp.SR.csMethodExpectsParams(), m))
        else 
            return usesTDC
    }

// This is a slight variation on ArgsMustSubsumeOrConvert that adds contextual error report to the
// subsumption check.  The two could likely be combines.
and ArgsMustSubsumeOrConvertWithContextualReport
        (csenv: ConstraintSolverEnv)
        ad
        ndeep
        trace
        cxsln 
        isConstraint
        calledMeth
        calledArg
        (callerArg: CallerArg<Expr>) = 
    trackErrors {
        let callerArgTy = callerArg.CallerArgumentType
        let m = callerArg.Range
        let calledArgTy, usesTDC, eqn = AdjustCalledArgType csenv.InfoReader ad isConstraint true calledArg callerArg
        match eqn with 
        | Some (ty1, ty2, msg) ->
            do! SolveTypeEqualsType csenv ndeep m trace cxsln ty1 ty2
            msg csenv.DisplayEnv
        | None -> ()
        match usesTDC with 
        | TypeDirectedConversionUsed.Yes(warn, _, _) -> do! WarnD(warn csenv.DisplayEnv)
        | TypeDirectedConversionUsed.No -> ()
        do! SolveTypeSubsumesTypeWithWrappedContextualReport csenv ndeep m trace cxsln (Some calledArg.CalledArgumentType) calledArgTy callerArgTy (fun e -> ArgDoesNotMatchError(e :?> _, calledMeth, calledArg, callerArg))  
        return usesTDC
    }

and TypesEquiv csenv ndeep trace cxsln ty1 ty2 = 
    trackErrors {
        do! SolveTypeEqualsTypeWithReport csenv ndeep csenv.m trace cxsln ty1 ty2
        return TypeDirectedConversionUsed.No
    }

and TypesMustSubsume (csenv: ConstraintSolverEnv) ndeep trace cxsln m calledArgTy callerArgTy = 
    trackErrors {
        do! SolveTypeSubsumesTypeWithReport csenv ndeep m trace cxsln None calledArgTy callerArgTy 
        return TypeDirectedConversionUsed.No
    }

and ReturnTypesMustSubsumeOrConvert (csenv: ConstraintSolverEnv) ad ndeep trace cxsln isConstraint m isMethodArg reqdTy actualTy = 
    trackErrors {
        let reqdTy, usesTDC, eqn = AdjustRequiredTypeForTypeDirectedConversions csenv.InfoReader ad isMethodArg isConstraint reqdTy actualTy m
        match eqn with 
        | Some (ty1, ty2, msg) ->
            do! SolveTypeEqualsType csenv ndeep m trace cxsln ty1 ty2 
            msg csenv.DisplayEnv
        | None -> ()
        match usesTDC with 
        | TypeDirectedConversionUsed.Yes(warn, _, _) -> do! WarnD(warn csenv.DisplayEnv)
        | TypeDirectedConversionUsed.No -> ()
        do! SolveTypeSubsumesTypeWithReport csenv ndeep m trace cxsln None reqdTy actualTy 
        return usesTDC
    }

and ArgsEquivOrConvert (csenv: ConstraintSolverEnv) ad ndeep trace cxsln isConstraint calledArg (callerArg: CallerArg<_>) = 
    trackErrors {
        let callerArgTy = callerArg.CallerArgumentType
        let m = callerArg.Range
        let calledArgTy, usesTDC, eqn = AdjustCalledArgType csenv.InfoReader ad isConstraint true calledArg callerArg
        match eqn with 
        | Some (ty1, ty2, msg) ->
            do! SolveTypeEqualsType csenv ndeep m trace cxsln ty1 ty2 
            msg csenv.DisplayEnv
        | None -> ()
        match usesTDC with 
        | TypeDirectedConversionUsed.Yes(warn, _, _) -> do! WarnD(warn csenv.DisplayEnv)
        | TypeDirectedConversionUsed.No -> ()
        if not (typeEquiv csenv.g calledArgTy callerArgTy) then 
            return! ErrorD(Error(FSComp.SR.csArgumentTypesDoNotMatch(), m))
        else
            return usesTDC
    }

and ReportNoCandidatesError (csenv: ConstraintSolverEnv) (nUnnamedCallerArgs, nNamedCallerArgs) methodName ad (calledMethGroup: CalledMeth<_> list) isSequential =

    let amap = csenv.amap
    let m    = csenv.m
    let denv = csenv.DisplayEnv
    let infoReader = csenv.InfoReader

    match (calledMethGroup |> List.partition (CalledMeth.GetMethod >> IsMethInfoAccessible amap m ad)), 
          (calledMethGroup |> List.partition (fun cmeth -> cmeth.HasCorrectObjArgs(m))), 
          (calledMethGroup |> List.partition (fun cmeth -> cmeth.HasCorrectArity)), 
          (calledMethGroup |> List.partition (fun cmeth -> cmeth.HasCorrectGenericArity)), 
          (calledMethGroup |> List.partition (fun cmeth -> cmeth.AssignsAllNamedArgs)) with

    // No version accessible 
    | ([], others), _, _, _, _ ->  
        if isNil others then
            Error (FSComp.SR.csMemberIsNotAccessible(methodName, (ShowAccessDomain ad)), m)
        else
            Error (FSComp.SR.csMemberIsNotAccessible2(methodName, (ShowAccessDomain ad)), m)
    | _, ([], cmeth :: _), _, _, _ ->  
    
        // Check all the argument types.
        if cmeth.CalledObjArgTys(m).Length <> 0 then
            Error (FSComp.SR.csMethodIsNotAStaticMethod(methodName), m)
        else
            Error (FSComp.SR.csMethodIsNotAnInstanceMethod(methodName), m)

    // One method, incorrect name/arg assignment 
    | _, _, _, _, ([], [cmeth]) -> 
        let minfo = cmeth.Method
        let msgNum, msgText = FSComp.SR.csRequiredSignatureIs(NicePrint.stringOfMethInfo infoReader m denv minfo)
        match cmeth.UnassignedNamedArgs with 
        | CallerNamedArg(id, _) :: _ -> 
            if minfo.IsConstructor then
                let suggestFields (addToBuffer: string -> unit) =
                    for p in minfo.DeclaringTyconRef.AllInstanceFieldsAsList do
                        addToBuffer(p.LogicalName.Replace("@", ""))

                ErrorWithSuggestions((msgNum, FSComp.SR.csCtorHasNoArgumentOrReturnProperty(methodName, id.idText, msgText)), id.idRange, id.idText, suggestFields)
            else
                Error((msgNum, FSComp.SR.csMemberHasNoArgumentOrReturnProperty(methodName, id.idText, msgText)), id.idRange)
        | [] -> Error((msgNum, msgText), m)

    // One method, incorrect number of arguments provided by the user
    | _, _, ([], [cmeth]), _, _ when not cmeth.HasCorrectArity ->  
        let minfo = cmeth.Method
        let nReqd = cmeth.TotalNumUnnamedCalledArgs
        let nActual = cmeth.TotalNumUnnamedCallerArgs
        let signature = NicePrint.stringOfMethInfo infoReader m denv minfo
        if nActual = nReqd then 
            let nreqdTyArgs = cmeth.NumCalledTyArgs
            let nactualTyArgs = cmeth.NumCallerTyArgs
            Error (FSComp.SR.csMemberSignatureMismatchArityType(methodName, nreqdTyArgs, nactualTyArgs, signature), m)
        else
            let nReqdNamed = cmeth.TotalNumAssignedNamedArgs

            if nReqdNamed = 0 && cmeth.NumAssignedProps = 0 then
                if minfo.IsConstructor then
                    let couldBeNameArgs =
                        cmeth.ArgSets
                        |> List.exists (fun argSet ->
                            argSet.UnnamedCallerArgs 
                            |> List.exists (fun c -> isSequential c.Expr))

                    if couldBeNameArgs then
                        Error (FSComp.SR.csCtorSignatureMismatchArityProp(methodName, nReqd, nActual, signature), m)
                    else
                        Error (FSComp.SR.csCtorSignatureMismatchArity(methodName, nReqd, nActual, signature), m)
                else
                    Error (FSComp.SR.csMemberSignatureMismatchArity(methodName, nReqd, nActual, signature), m)
            else
                if nReqd > nActual then
                    let diff = nReqd - nActual
                    let missingArgs = List.skip nReqd cmeth.AllUnnamedCalledArgs
                    match NamesOfCalledArgs missingArgs with 
                    | [] ->
                        if nActual = 0 then 
                            Error (FSComp.SR.csMemberSignatureMismatch(methodName, diff, signature), m)
                        else 
                            Error (FSComp.SR.csMemberSignatureMismatch2(methodName, diff, signature), m)
                    | names -> 
                        let str = String.concat ";" (pathOfLid names)
                        if nActual = 0 then 
                            Error (FSComp.SR.csMemberSignatureMismatch3(methodName, diff, signature, str), m)
                        else 
                            Error (FSComp.SR.csMemberSignatureMismatch4(methodName, diff, signature, str), m)
                else 
                    Error (FSComp.SR.csMemberSignatureMismatchArityNamed(methodName, (nReqd+nReqdNamed), nActual, nReqdNamed, signature), m)

    // One or more accessible, all the same arity, none correct 
    | (cmeth :: cmeths2, _), _, _, _, _ when not cmeth.HasCorrectArity && cmeths2 |> List.forall (fun cmeth2 -> cmeth.TotalNumUnnamedCalledArgs = cmeth2.TotalNumUnnamedCalledArgs) -> 
        Error (FSComp.SR.csMemberNotAccessible(methodName, nUnnamedCallerArgs, methodName, cmeth.TotalNumUnnamedCalledArgs), m)
    // Many methods, all with incorrect number of generic arguments
    | _, _, _, ([], cmeth :: _), _ -> 
        let msg = FSComp.SR.csIncorrectGenericInstantiation((ShowAccessDomain ad), methodName, cmeth.NumCallerTyArgs)
        Error (msg, m)
    // Many methods of different arities, all incorrect 
    | _, _, ([], cmeth :: _), _, _ -> 
        let minfo = cmeth.Method
        Error (FSComp.SR.csMemberOverloadArityMismatch(methodName, cmeth.TotalNumUnnamedCallerArgs, (List.sum minfo.NumArgs)), m)
    | _ -> 
        let msg = 
            if nNamedCallerArgs = 0 then 
                FSComp.SR.csNoMemberTakesTheseArguments((ShowAccessDomain ad), methodName, nUnnamedCallerArgs)
            else 
                let s = calledMethGroup |> List.map (fun cmeth -> cmeth.UnassignedNamedArgs |> List.map (fun na -> na.Name)|> Set.ofList) |> Set.intersectMany
                if s.IsEmpty then 
                    FSComp.SR.csNoMemberTakesTheseArguments2((ShowAccessDomain ad), methodName, nUnnamedCallerArgs, nNamedCallerArgs)
                else 
                    let sample = s.MinimumElement
                    FSComp.SR.csNoMemberTakesTheseArguments3((ShowAccessDomain ad), methodName, nUnnamedCallerArgs, sample)
        Error (msg, m)
    |> ErrorD

and ReportNoCandidatesErrorExpr csenv callerArgCounts methodName ad calledMethGroup =
    let isSequential e = match stripDebugPoints e with Expr.Sequential _ -> true | _ -> false
    ReportNoCandidatesError csenv callerArgCounts methodName ad calledMethGroup isSequential

and ReportNoCandidatesErrorSynExpr csenv callerArgCounts methodName ad calledMethGroup =
    let isSequential e = match e with SynExpr.Sequential _ -> true | _ -> false
    ReportNoCandidatesError csenv callerArgCounts methodName ad calledMethGroup isSequential

/// When checking whether a method solves a trait constraint, we can assume the trait is solved
/// by that method for the purposes of further type checking (just as we assume a type equation
/// for the purposes of checking constraints arising from that type equation).
///
/// In F# 5.0 and 6.0 we assert this late by passing the cxsln parameter around. However this
/// relies on not checking return types for SRTP constraints eagerly
and AssumeMethodSolvesTrait (csenv: ConstraintSolverEnv) (cx: TraitConstraintInfo option) m _trace (calledMeth: CalledMeth<_>) = 
    match cx with
    | Some traitInfo when traitInfo.Solution.IsNone -> 
        let staticTyOpt = if calledMeth.Method.IsInstance then None else calledMeth.OptionalStaticType
        let traitSln = MemberConstraintSolutionOfMethInfo csenv.SolverState m calledMeth.Method calledMeth.CalledTyArgs staticTyOpt
        Some (traitInfo, traitSln)
    | _ -> 
        None

// Resolve the overloading of a method 
// This is used after analyzing the types of arguments 
and ResolveOverloading 
         (csenv: ConstraintSolverEnv) 
         trace           // The undo trace, if any
         methodName      // The name of the method being called, for error reporting
         ndeep           // Depth of inference
         cx              // We're doing overload resolution as part of constraint solving, where special rules apply for op_Explicit and op_Implicit constraints.
         (callerArgs: CallerArgs<Expr>)
         ad              // The access domain of the caller, e.g. a module, type etc. 
         calledMethGroup // The set of methods being called 
         permitOptArgs   // Can we supply optional arguments?
         (reqdRetTyOpt: OverallTy option) // The expected return type, if known 
         : CalledMeth<Expr> option * OperationResult<unit>
     =
    let g = csenv.g
    let infoReader = csenv.InfoReader
    let m    = csenv.m

    let isOpConversion =
        (methodName = "op_Explicit") ||
        (methodName = "op_Implicit")

    // See what candidates we have based on name and arity 
    let candidates = calledMethGroup |> List.filter (fun cmeth -> cmeth.IsCandidate(m, ad))

    let calledMethOpt, errors, calledMethTrace = 
        match calledMethGroup, candidates with 
        | _, [calledMeth] when not isOpConversion ->
            // See what candidates we have based on static/virtual/abstract
            
            // If false then is a static method call directly on an interface e.g.
            // IParsable.Parse(...)
            // IAdditionOperators.(+)
            // This is not allowed as Parse and (+) method are static abstract
            let isStaticConstrainedCall =
                match calledMeth.OptionalStaticType with
                | Some ttype -> isTyparTy g ttype
                | None -> false
                
            match calledMeth.Method with
            | ILMeth(ilMethInfo= ilMethInfo) when not isStaticConstrainedCall && ilMethInfo.IsStatic && ilMethInfo.IsAbstract ->
                None, ErrorD (Error (FSComp.SR.chkStaticAbstractInterfaceMembers(ilMethInfo.ILName), m)), NoTrace
            | _ -> Some calledMeth, CompleteD, NoTrace

        | [], _ when not isOpConversion -> 
            None, ErrorD (Error (FSComp.SR.csMethodNotFound(methodName), m)), NoTrace

        | _, [] when not isOpConversion -> 
            None, ReportNoCandidatesErrorExpr csenv callerArgs.CallerArgCounts methodName ad calledMethGroup, NoTrace
            
        | _, _ -> 

          // Always take the return type into account for
          //    -- op_Explicit, op_Implicit
          //    -- candidate method sets that potentially use tupling of unfilled out args
          let alwaysCheckReturn =
              isOpConversion ||
              candidates |> List.exists (fun cmeth -> cmeth.HasOutArgs) 

          // Exact match rule.
          //
          // See what candidates we have based on current inferred type information 
          // and exact matches of argument types. 
          let exactMatchCandidates =
              candidates |> FilterEachThenUndo (fun newTrace calledMeth -> 
                    let csenv = { csenv with IsSpeculativeForMethodOverloading = true }
                    let cxsln = AssumeMethodSolvesTrait csenv cx m (WithTrace newTrace) calledMeth
                    CanMemberSigsMatchUpToCheck 
                        csenv 
                        permitOptArgs 
                        alwaysCheckReturn
                        (TypesEquiv csenv ndeep (WithTrace newTrace) cxsln)  // instantiations equivalent
                        (TypesMustSubsume csenv ndeep (WithTrace newTrace) cxsln m) // obj can subsume
                        (ReturnTypesMustSubsumeOrConvert csenv ad ndeep (WithTrace newTrace) cxsln cx.IsSome m) // return can subsume or convert
                        (ArgsEquivOrConvert csenv ad ndeep (WithTrace newTrace) cxsln cx.IsSome)  // args exact
                        reqdRetTyOpt 
                        calledMeth)

          match exactMatchCandidates with
          | [(calledMeth, warns, _, _usesTDC)] ->
               Some calledMeth, OkResult (warns, ()), NoTrace

          | _ -> 
            // Now determine the applicable methods.
            // Subsumption on arguments is allowed.
            let applicable =
                candidates |> FilterEachThenUndo (fun newTrace candidate -> 
                    let csenv = { csenv with IsSpeculativeForMethodOverloading = true }
                    let cxsln = AssumeMethodSolvesTrait csenv cx m (WithTrace newTrace) candidate
                    CanMemberSigsMatchUpToCheck 
                        csenv 
                        permitOptArgs
                        alwaysCheckReturn
                        (TypesEquiv csenv ndeep (WithTrace newTrace) cxsln)  // instantiations equivalent
                        (TypesMustSubsume csenv ndeep (WithTrace newTrace) cxsln m) // obj can subsume
                        (ReturnTypesMustSubsumeOrConvert csenv ad ndeep (WithTrace newTrace) cxsln cx.IsSome m) // return can subsume or convert
                        (ArgsMustSubsumeOrConvertWithContextualReport csenv ad ndeep (WithTrace newTrace) cxsln cx.IsSome candidate)  // args can subsume
                        reqdRetTyOpt 
                        candidate)

            match applicable with 
            | [] ->
                // OK, we failed. Collect up the errors from overload resolution and the possible overloads
                let errors = 
                    candidates 
                    |> List.choose (fun calledMeth -> 
                            match CollectThenUndo (fun newTrace -> 
                                         let csenv = { csenv with IsSpeculativeForMethodOverloading = true }
                                         let cxsln = AssumeMethodSolvesTrait csenv cx m (WithTrace newTrace) calledMeth
                                         CanMemberSigsMatchUpToCheck 
                                             csenv 
                                             permitOptArgs
                                             alwaysCheckReturn
                                             (TypesEquiv csenv ndeep (WithTrace newTrace) cxsln) 
                                             (TypesMustSubsume csenv ndeep (WithTrace newTrace) cxsln m)
                                             (ReturnTypesMustSubsumeOrConvert csenv ad ndeep (WithTrace newTrace) cxsln cx.IsSome m)
                                             (ArgsMustSubsumeOrConvertWithContextualReport csenv ad ndeep (WithTrace newTrace) cxsln cx.IsSome calledMeth) 
                                             reqdRetTyOpt 
                                             calledMeth) with 
                            | OkResult _ -> None
                            | ErrorResult(_warnings, exn) ->
                                Some {methodSlot = calledMeth; infoReader = infoReader; error = exn })

                let err = FailOverloading csenv calledMethGroup reqdRetTyOpt isOpConversion callerArgs (NoOverloadsFound (methodName, errors, cx)) m

                None, ErrorD err, NoTrace

            | [(calledMeth, warns, t, _usesTDC)] ->
                Some calledMeth, OkResult (warns, ()), WithTrace t

            | applicableMeths -> 
                GetMostApplicableOverload csenv ndeep candidates applicableMeths calledMethGroup reqdRetTyOpt isOpConversion callerArgs methodName cx m

    // If we've got a candidate solution: make the final checks - no undo here! 
    // Allow subsumption on arguments. Include the return type.
    // Unify return types.
    match calledMethOpt with 
    | Some calledMeth ->
    
        // Static IL interfaces methods are not supported in lower F# versions.
        if calledMeth.Method.IsILMethod && not calledMeth.Method.IsInstance && isInterfaceTy g calledMeth.Method.ApparentEnclosingType then
            checkLanguageFeatureRuntimeAndRecover csenv.InfoReader LanguageFeature.DefaultInterfaceMemberConsumption m
            checkLanguageFeatureAndRecover g.langVersion LanguageFeature.DefaultInterfaceMemberConsumption m

        calledMethOpt, 
        trackErrors {
                        do! errors
                        let cxsln = AssumeMethodSolvesTrait csenv cx m trace calledMeth
                        match calledMethTrace with
                        | NoTrace ->
                           let! _usesTDC =
                            CanMemberSigsMatchUpToCheck 
                                 csenv 
                                 permitOptArgs
                                 true
                                 (TypesEquiv csenv ndeep trace cxsln) // instantiations equal
                                 (TypesMustSubsume csenv ndeep trace cxsln m) // obj can subsume
                                 (ReturnTypesMustSubsumeOrConvert csenv ad ndeep trace cxsln cx.IsSome m) // return can subsume or convert
                                 (ArgsMustSubsumeOrConvert csenv ad ndeep trace cxsln cx.IsSome true)  // args can subsume or convert
                                 reqdRetTyOpt 
                                 calledMeth
                           return ()
                        | WithTrace calledMethTrc ->

                            // Re-play existing trace
                            trace.AddFromReplay calledMethTrc

                            // Unify return type
                            match reqdRetTyOpt with 
                            | None -> () 
                            | Some reqdRetTy -> 
                                let actualRetTy = calledMeth.CalledReturnTypeAfterOutArgTupling
                                if isByrefTy g reqdRetTy.Commit then 
                                    return! ErrorD(Error(FSComp.SR.tcByrefReturnImplicitlyDereferenced(), m))
                                else
                                    match reqdRetTy with
                                    | MustConvertTo(isMethodArg, reqdRetTy) when g.langVersion.SupportsFeature LanguageFeature.AdditionalTypeDirectedConversions ->
                                        let! _usesTDC = ReturnTypesMustSubsumeOrConvert csenv ad ndeep trace cxsln isMethodArg m isMethodArg reqdRetTy actualRetTy
                                        return ()
                                    | _ ->
                                        let! _usesTDC = TypesEquiv csenv ndeep trace cxsln reqdRetTy.Commit actualRetTy
                                        return ()

        }

    | None -> 
        None, errors        

and FailOverloading csenv calledMethGroup reqdRetTyOpt isOpConversion callerArgs overloadResolutionFailure m = 
    let denv = csenv.DisplayEnv
    // Try to extract information to give better error for ambiguous op_Explicit and op_Implicit 
    let convOpData = 
        if isOpConversion then 
            match calledMethGroup, reqdRetTyOpt with 
            | h :: _, Some reqdRetTy -> 
                Some (h.Method.ApparentEnclosingType, reqdRetTy)
            | _ -> None 
        else
            None

    match convOpData with 
    | Some (fromTy, toTy) -> 
        UnresolvedConversionOperator (denv, fromTy, toTy.Commit, m)
    | None -> 
        // Otherwise pass the overload resolution failure for error printing in CompileOps
        UnresolvedOverloading (denv, callerArgs, overloadResolutionFailure, m)

and GetMostApplicableOverload csenv ndeep candidates applicableMeths calledMethGroup reqdRetTyOpt isOpConversion callerArgs methodName cx m =
    let g = csenv.g
    let infoReader = csenv.InfoReader
    /// Compare two things by the given predicate. 
    /// If the predicate returns true for x1 and false for x2, then x1 > x2
    /// If the predicate returns false for x1 and true for x2, then x1 < x2
    /// Otherwise x1 = x2
                
    // Note: Relies on 'compare' respecting true > false
    let compareCond (p: 'T -> 'T -> bool) x1 x2 = 
        compare (p x1 x2) (p x2 x1)

    /// Compare types under the feasibly-subsumes ordering
    let compareTypes ty1 ty2 = 
        (ty1, ty2) ||> compareCond (fun x1 x2 -> TypeFeasiblySubsumesType ndeep csenv.g csenv.amap m x2 CanCoerce x1) 

    /// Compare arguments under the feasibly-subsumes ordering and the adhoc Func-is-better-than-other-delegates rule
    let compareArg (calledArg1: CalledArg) (calledArg2: CalledArg) =
        let c = compareTypes calledArg1.CalledArgumentType calledArg2.CalledArgumentType
        if c <> 0 then c else

        let c = 
            (calledArg1.CalledArgumentType, calledArg2.CalledArgumentType) ||> compareCond (fun ty1 ty2 -> 

                // Func<_> is always considered better than any other delegate type
                match tryTcrefOfAppTy csenv.g ty1 with 
                | ValueSome tcref1 when 
                    tcref1.DisplayName = "Func" &&  
                    (match tcref1.PublicPath with Some p -> p.EnclosingPath = [| "System" |] | _ -> false) && 
                    isDelegateTy g ty1 &&
                    isDelegateTy g ty2 -> true

                // T is always better than inref<T>
                | _ when isInByrefTy csenv.g ty2 && typeEquiv csenv.g ty1 (destByrefTy csenv.g ty2) -> 
                    true

                // T is always better than Nullable<T> from F# 5.0 onwards
                | _ when g.langVersion.SupportsFeature(LanguageFeature.NullableOptionalInterop) &&
                            isNullableTy csenv.g ty2 &&
                            typeEquiv csenv.g ty1 (destNullableTy csenv.g ty2) -> 
                    true

                | _ -> false)

        if c <> 0 then c else
        0

    /// Check whether one overload is better than another
    let better (candidate: CalledMeth<_>, candidateWarnings, _, usesTDC1) (other: CalledMeth<_>, otherWarnings, _, usesTDC2) =
        let candidateWarnCount = List.length candidateWarnings
        let otherWarnCount = List.length otherWarnings

        // Prefer methods that don't use type-directed conversion
        let c = compare (match usesTDC1 with TypeDirectedConversionUsed.No -> 1 | _ -> 0) (match usesTDC2 with TypeDirectedConversionUsed.No -> 1 | _ -> 0)
        if c <> 0 then c else
            
        // Prefer methods that need less type-directed conversion
        let c = compare (match usesTDC1 with TypeDirectedConversionUsed.Yes(_, false, _) -> 1 | _ -> 0) (match usesTDC2 with TypeDirectedConversionUsed.Yes(_, false, _) -> 1 | _ -> 0)
        if c <> 0 then c else

        // Prefer methods that only have nullable type-directed conversions
        let c = compare (match usesTDC1 with TypeDirectedConversionUsed.Yes(_, _, true) -> 1 | _ -> 0) (match usesTDC2 with TypeDirectedConversionUsed.Yes(_, _, true) -> 1 | _ -> 0)
        if c <> 0 then c else

        // Prefer methods that don't give "this code is less generic" warnings
        // Note: Relies on 'compare' respecting true > false
        let c = compare (candidateWarnCount = 0) (otherWarnCount = 0)
        if c <> 0 then c else

        // Prefer methods that don't use param array arg
        // Note: Relies on 'compare' respecting true > false
        let c =  compare (not candidate.UsesParamArrayConversion) (not other.UsesParamArrayConversion) 
        if c <> 0 then c else

        // Prefer methods with more precise param array arg type
        let c = 
            if candidate.UsesParamArrayConversion && other.UsesParamArrayConversion then
                compareTypes (candidate.GetParamArrayElementType()) (other.GetParamArrayElementType())
            else
                0
        if c <> 0 then c else

        // Prefer methods that don't use out args
        // Note: Relies on 'compare' respecting true > false
        let c = compare (not candidate.HasOutArgs) (not other.HasOutArgs)
        if c <> 0 then c else

        // Prefer methods that don't use optional args
        // Note: Relies on 'compare' respecting true > false
        let c = compare (not candidate.HasOptionalArgs) (not other.HasOptionalArgs)
        if c <> 0 then c else

        // check regular unnamed args. The argument counts will only be different if one is using param args
        let c = 
            if candidate.TotalNumUnnamedCalledArgs = other.TotalNumUnnamedCalledArgs then
                // For extension members, we also include the object argument type, if any in the comparison set
                // This matches C#, where all extension members are treated and resolved as "static" methods calls
                let cs = 
                    (if candidate.Method.IsExtensionMember && other.Method.IsExtensionMember then 
                        let objArgTys1 = candidate.CalledObjArgTys(m) 
                        let objArgTys2 = other.CalledObjArgTys(m) 
                        if objArgTys1.Length = objArgTys2.Length then 
                            List.map2 compareTypes objArgTys1 objArgTys2
                        else
                            []
                     else 
                        []) @
                    ((candidate.AllUnnamedCalledArgs, other.AllUnnamedCalledArgs) ||> List.map2 compareArg) 
                // "all args are at least as good, and one argument is actually better"
                if cs |> List.forall (fun x -> x >= 0) && cs |> List.exists (fun x -> x > 0) then 
                    1
                // "all args are at least as bad, and one argument is actually worse"
                elif cs |> List.forall (fun x -> x <= 0) && cs |> List.exists (fun x -> x < 0) then 
                    -1
                // "argument lists are incomparable"
                else
                    0
            else
                0
        if c <> 0 then c else

        // prefer non-extension methods 
        let c = compare (not candidate.Method.IsExtensionMember) (not other.Method.IsExtensionMember)
        if c <> 0 then c else

        // between extension methods, prefer most recently opened
        let c = 
            if candidate.Method.IsExtensionMember && other.Method.IsExtensionMember then 
                compare candidate.Method.ExtensionMemberPriority other.Method.ExtensionMemberPriority 
            else 
                0
        if c <> 0 then c else

        // Prefer non-generic methods 
        // Note: Relies on 'compare' respecting true > false
        let c = compare candidate.CalledTyArgs.IsEmpty other.CalledTyArgs.IsEmpty
        if c <> 0 then c else

        // F# 5.0 rule - prior to F# 5.0 named arguments (on the caller side) were not being taken 
        // into account when comparing overloads.  So adding a name to an argument might mean 
        // overloads could no longer be distinguished.  We thus look at *all* arguments (whether
        // optional or not) as an additional comparison technique.
        let c = 
            if g.langVersion.SupportsFeature(LanguageFeature.NullableOptionalInterop) then
                let cs = 
                    let args1 = candidate.AllCalledArgs |> List.concat
                    let args2 = other.AllCalledArgs |> List.concat
                    if args1.Length = args2.Length then 
                        (args1, args2) ||> List.map2 compareArg
                    else
                        []
                // "all args are at least as good, and one argument is actually better"
                if cs |> List.forall (fun x -> x >= 0) && cs |> List.exists (fun x -> x > 0) then 
                    1
                // "all args are at least as bad, and one argument is actually worse"
                elif cs |> List.forall (fun x -> x <= 0) && cs |> List.exists (fun x -> x < 0) then 
                    -1
                // "argument lists are incomparable"
                else
                    0
            else
                0
        if c <> 0 then c else

        // Properties are kept incl. almost-duplicates because of the partial-override possibility.
        // E.g. base can have get,set and derived only get => we keep both props around until method resolution time.
        // Now is the type to pick the better (more derived) one.
        match candidate.AssociatedPropertyInfo,other.AssociatedPropertyInfo,candidate.Method.IsExtensionMember,other.Method.IsExtensionMember with
        | Some p1, Some p2, false, false -> compareTypes p1.ApparentEnclosingType p2.ApparentEnclosingType
        | _ -> 0
        

    let bestMethods =
        let indexedApplicableMeths = applicableMeths |> List.indexed
        indexedApplicableMeths |> List.choose (fun (i, candidate) -> 
            if indexedApplicableMeths |> List.forall (fun (j, other) -> 
                    i = j ||
                    let res = better candidate other
                    res > 0) then 
                Some candidate
            else 
                None) 

    match bestMethods with 
    | [(calledMeth, warns, t, _)] ->
        Some calledMeth, OkResult (warns, ()), WithTrace t

    | bestMethods -> 
        let methods = 
            let getMethodSlotsAndErrors methodSlot errors =
                [ match errors with
                  | [] -> 
                      { methodSlot = methodSlot; error = Unchecked.defaultof<exn>; infoReader = infoReader }
                  | errors ->
                      for error in errors do 
                          { methodSlot = methodSlot; error = error; infoReader = infoReader } ]

            // use the most precise set
            // - if after filtering bestMethods still contains something - use it
            // - otherwise use applicableMeths or initial set of candidate methods
            [ match bestMethods with
                | [] -> 
                    match applicableMeths with
                    | [] -> for methodSlot in candidates do yield getMethodSlotsAndErrors methodSlot []
                    | m -> for methodSlot, errors, _, _ in m do yield getMethodSlotsAndErrors methodSlot errors
                | m -> for methodSlot, errors, _, _ in m do yield getMethodSlotsAndErrors methodSlot errors ]

        let methods = List.concat methods

        let err = FailOverloading csenv calledMethGroup reqdRetTyOpt isOpConversion callerArgs (PossibleCandidates(methodName, methods,cx)) m
        None, ErrorD err, NoTrace

let ResolveOverloadingForCall denv css m  methodName callerArgs ad calledMethGroup permitOptArgs reqdRetTy =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    ResolveOverloading csenv NoTrace methodName 0 None callerArgs ad calledMethGroup permitOptArgs (Some reqdRetTy)

/// This is used before analyzing the types of arguments in a single overload resolution
let UnifyUniqueOverloading 
         denv
         css 
         m 
         callerArgCounts 
         methodName 
         ad 
         (calledMethGroup: CalledMeth<SynExpr> list) 
         reqdRetTy    // The expected return type, if known 
   =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    let m = csenv.m
    // See what candidates we have based on name and arity 
    let candidates = calledMethGroup |> List.filter (fun cmeth -> cmeth.IsCandidate(m, ad)) 
    let ndeep = 0
    match calledMethGroup, candidates with 
    | _, [calledMeth] ->  trackErrors {
      let! _usesTDC =
        // Only one candidate found - we thus know the types we expect of arguments 
        CanMemberSigsMatchUpToCheck 
            csenv 
            true // permitOptArgs
            true // always check return type
            (TypesEquiv csenv ndeep NoTrace None) 
            (TypesMustSubsume csenv ndeep NoTrace None m)
            (ReturnTypesMustSubsumeOrConvert csenv ad ndeep NoTrace None false m)
            (ArgsMustSubsumeOrConvert csenv ad ndeep NoTrace None false false)
            (Some reqdRetTy)
            calledMeth
      return true
     }
        
    | [], _ -> 
        ErrorD (Error (FSComp.SR.csMethodNotFound(methodName), m))
    | _, [] -> trackErrors {
        do! ReportNoCandidatesErrorSynExpr csenv callerArgCounts methodName ad calledMethGroup 
        return false
      }
    | _ -> 
        ResultD false

/// Re-assess the staticness of the type parameters. Necessary prior to assessing generalization.
let UpdateStaticReqOfTypar (denv: DisplayEnv) css m (trace: OptionalTrace) (typar: Typar) =
    let g = denv.g
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    trackErrors {
        if g.langVersion.SupportsFeature LanguageFeature.InterfacesWithAbstractStaticMembers then
            for cx in typar.Constraints do
                match cx with
                | TyparConstraint.MayResolveMember(traitInfo,_) ->
                    for supportTy in traitInfo.SupportTypes do
                        do! SolveTypStaticReq csenv trace TyparStaticReq.HeadType supportTy
                | TyparConstraint.SimpleChoice _ ->
                        do! SolveTypStaticReqTypar csenv trace TyparStaticReq.HeadType typar
                | _ -> ()
    } |> RaiseOperationResult

/// Remove the global constraints related to generalized type variables
let EliminateConstraintsForGeneralizedTypars (denv: DisplayEnv) css m (trace: OptionalTrace) (generalizedTypars: Typars) =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv

    for tp in generalizedTypars do
        let tpn = tp.Stamp
        let cxst = csenv.SolverState.ExtraCxs
        let cxs = cxst.FindAll tpn
        for cx in cxs do 
            trace.Exec
                (fun () -> cxst.Remove tpn)
                (fun () -> (csenv.SolverState.ExtraCxs.Add (tpn, cx)))


//-------------------------------------------------------------------------
// Main entry points to constraint solver (some backdoors are used for 
// some constructs)
//
// No error recovery here: we do that on a per-expression basis.
//------------------------------------------------------------------------- 

let AddCxTypeEqualsType contextInfo denv css m expected actual =
    let csenv = MakeConstraintSolverEnv contextInfo css m denv
    PostponeOnFailedMemberConstraintResolution csenv NoTrace
        (fun csenv -> SolveTypeEqualsTypeWithReport csenv 0 m NoTrace None expected actual)
        ErrorD
    |> RaiseOperationResult

let UndoIfFailed f =
    let trace = Trace.New()
    let res = 
        try 
            f trace 
            |> CheckNoErrorsAndGetWarnings
        with e -> None
    match res with 
    | None -> 
        // Don't report warnings if we failed
        trace.Undo()
        false
    | Some (warns, _) -> 
        // Report warnings if we succeeded
        ReportWarnings warns
        true

let UndoIfFailedOrWarnings f =
    let trace = Trace.New()
    let res = 
        try 
            f trace 
            |> CheckNoErrorsAndGetWarnings
        with _ -> None
    match res with 
    | Some ([], _)-> 
        true
    | _ -> 
        trace.Undo()
        false

let AddCxTypeEqualsTypeUndoIfFailed denv css m ty1 ty2 =
    UndoIfFailed (fun trace -> 
     let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
     let csenv = { csenv with ErrorOnFailedMemberConstraintResolution = true }
     SolveTypeEqualsTypeKeepAbbrevs csenv 0 m (WithTrace trace) ty1 ty2)

let AddCxTypeEqualsTypeUndoIfFailedOrWarnings denv css m ty1 ty2 =
    UndoIfFailedOrWarnings (fun trace -> 
        let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
        let csenv = { csenv with ErrorOnFailedMemberConstraintResolution = true }
        SolveTypeEqualsTypeKeepAbbrevs csenv 0 m (WithTrace trace) ty1 ty2)

let AddCxTypeEqualsTypeMatchingOnlyUndoIfFailed denv css m ty1 ty2 =
    UndoIfFailed (fun trace -> 
        let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
        let csenv = { csenv with MatchingOnly = true; ErrorOnFailedMemberConstraintResolution = true }
        SolveTypeEqualsTypeKeepAbbrevs csenv 0 m (WithTrace trace) ty1 ty2)

let AddCxTypeMustSubsumeTypeUndoIfFailed denv css m ty1 ty2 = 
    UndoIfFailed (fun trace ->
        let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
        let csenv = { csenv with ErrorOnFailedMemberConstraintResolution = true }
        SolveTypeSubsumesTypeKeepAbbrevs csenv 0 m (WithTrace trace) None ty1 ty2)

let AddCxTypeMustSubsumeTypeMatchingOnlyUndoIfFailed denv css m extraRigidTypars ty1 ty2 = 
    UndoIfFailed (fun trace ->
        let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
        let csenv = { csenv with MatchingOnly = true; ErrorOnFailedMemberConstraintResolution = true; ExtraRigidTypars=extraRigidTypars }
        SolveTypeSubsumesTypeKeepAbbrevs csenv 0 m (WithTrace trace) None ty1 ty2)

let AddCxTypeMustSubsumeType contextInfo denv css m trace ty1 ty2 = 
    let csenv = MakeConstraintSolverEnv contextInfo css m denv
    SolveTypeSubsumesTypeWithReport csenv 0 m trace None None ty1 ty2
    |> RaiseOperationResult

let AddCxMethodConstraint denv css m trace traitInfo  =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    PostponeOnFailedMemberConstraintResolution csenv trace
        (fun csenv ->
            trackErrors {
                do! 
                    SolveMemberConstraint csenv true PermitWeakResolution.No 0 m trace traitInfo
                    |> OperationResult.ignore
            })
        (fun res -> ErrorD (ErrorFromAddingConstraint(denv, res, m)))
    |> RaiseOperationResult

let AddCxTypeDefnNotSupportsNull denv css m trace ty =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    PostponeOnFailedMemberConstraintResolution csenv trace
        (fun csenv -> SolveTypeUseNotSupportsNull csenv 0 m trace ty)
        (fun res -> ErrorD (ErrorFromAddingConstraint(denv, res, m)))
    |> RaiseOperationResult

let AddCxTypeUseSupportsNull denv css m trace ty =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    PostponeOnFailedMemberConstraintResolution csenv trace
        (fun csenv -> SolveTypeUseSupportsNull csenv 0 m trace ty)
        (fun res -> ErrorD (ErrorFromAddingConstraint(denv, res, m)))
    |> RaiseOperationResult

let AddCxTypeCanCarryNullnessInfo denv css m ty nullness =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    let canCarryNullnessCheck() = SolveTypeCanCarryNullness csenv ty nullness |> RaiseOperationResult
    csenv.SolverState.PushPostInferenceCheck (preDefaults=false, check = canCarryNullnessCheck)


let AddCxTypeMustSupportComparison denv css m trace ty =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    PostponeOnFailedMemberConstraintResolution csenv trace
        (fun csenv -> SolveTypeSupportsComparison csenv 0 m trace ty)
        (fun res -> ErrorD (ErrorFromAddingConstraint(denv, res, m)))
    |> RaiseOperationResult

let AddCxTypeMustSupportEquality denv css m trace ty =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    PostponeOnFailedMemberConstraintResolution csenv trace
        (fun csenv -> SolveTypeSupportsEquality csenv 0 m trace ty)
        (fun res -> ErrorD (ErrorFromAddingConstraint(denv, res, m)))
    |> RaiseOperationResult

let AddCxTypeMustSupportDefaultCtor denv css m trace ty =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    PostponeOnFailedMemberConstraintResolution csenv trace
        (fun csenv -> SolveTypeRequiresDefaultConstructor csenv 0 m trace ty)
        (fun res -> ErrorD (ErrorFromAddingConstraint(denv, res, m)))
    |> RaiseOperationResult

let AddCxTypeIsReferenceType denv css m trace ty =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    PostponeOnFailedMemberConstraintResolution csenv trace
        (fun csenv -> SolveTypeIsReferenceType csenv 0 m trace ty)
        (fun res -> ErrorD (ErrorFromAddingConstraint(denv, res, m)))
    |> RaiseOperationResult

let AddCxTypeIsValueType denv css m trace ty =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    PostponeOnFailedMemberConstraintResolution csenv trace
        (fun csenv -> SolveTypeIsNonNullableValueType csenv 0 m trace ty)
        (fun res -> ErrorD (ErrorFromAddingConstraint(denv, res, m)))
    |> RaiseOperationResult
    
let AddCxTypeIsUnmanaged denv css m trace ty =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    PostponeOnFailedMemberConstraintResolution csenv trace
        (fun csenv -> SolveTypeIsUnmanaged csenv 0 m trace ty)
        (fun res -> ErrorD (ErrorFromAddingConstraint(denv, res, m)))
    |> RaiseOperationResult

let AddCxTypeIsEnum denv css m trace ty underlying =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    PostponeOnFailedMemberConstraintResolution csenv trace
        (fun csenv -> SolveTypeIsEnum csenv 0 m trace ty underlying)
        (fun res -> ErrorD (ErrorFromAddingConstraint(denv, res, m)))
    |> RaiseOperationResult

let AddCxTypeIsDelegate denv css m trace ty aty bty =
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    PostponeOnFailedMemberConstraintResolution csenv trace
        (fun csenv -> SolveTypeIsDelegate csenv 0 m trace ty aty bty)
        (fun res -> ErrorD (ErrorFromAddingConstraint(denv, res, m)))
    |> RaiseOperationResult

let AddCxTyparDefaultsTo denv css m ctxtInfo tp ridx ty =
    let csenv = MakeConstraintSolverEnv ctxtInfo css m denv
    PostponeOnFailedMemberConstraintResolution csenv NoTrace
        (fun csenv -> AddConstraint csenv 0 m NoTrace tp (TyparConstraint.DefaultsTo(ridx, ty, m)))
        (fun res -> ErrorD (ErrorFromAddingConstraint(denv, res, m)))
    |> RaiseOperationResult

let SolveTypeAsError denv css m ty =
    let ty2 = NewErrorType ()
    assert (destTyparTy css.g ty2).IsFromError
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    SolveTypeEqualsTypeKeepAbbrevs csenv 0 m NoTrace ty ty2 |> ignore
    
let ApplyTyparDefaultAtPriority denv css priority (tp: Typar) =
    tp.Constraints |> List.iter (fun tpc -> 
        match tpc with 
        | TyparConstraint.DefaultsTo(priority2, ty2, m) when priority2 = priority -> 
            let ty1 = mkTyparTy tp
            if not tp.IsSolved && not (typeEquiv css.g ty1 ty2) then
                let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
                PostponeOnFailedMemberConstraintResolution csenv NoTrace
                    (fun csenv ->
                        SolveTyparEqualsType csenv 0 m NoTrace ty1 ty2)
                    (fun res -> 
                        SolveTypeAsError denv css m ty1
                        ErrorD(ErrorFromApplyingDefault(css.g, denv, tp, ty2, res, m)))
                |> RaiseOperationResult
        | _ -> ())

let CreateCodegenState tcVal g amap = 
    { g = g
      amap = amap
      TcVal = tcVal
      ExtraCxs = HashMultiMap(10, HashIdentity.Structural)
      InfoReader = InfoReader(g, amap)
      PostInferenceChecksPreDefaults = ResizeArray() 
      PostInferenceChecksFinal = ResizeArray()
      WarnWhenUsingWithoutNullOnAWithNullTarget = None}

/// Determine if a codegen witness for a trait will require witness args to be available, e.g. in generic code
let CodegenWitnessExprForTraitConstraintWillRequireWitnessArgs tcVal g amap m (traitInfo:TraitConstraintInfo) =
    trackErrors {
        let css = CreateCodegenState tcVal g amap
        let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m (DisplayEnv.Empty g)

        let! _res = SolveMemberConstraint csenv true PermitWeakResolution.Yes 0 m NoTrace traitInfo

        let res =
            match traitInfo.Solution with
            | None
            | Some BuiltInSln -> true
            | _ -> false
        return res
    }

/// Generate a witness expression if none is otherwise available, e.g. in legacy non-witness-passing code
let CodegenWitnessExprForTraitConstraint tcVal g amap m (traitInfo:TraitConstraintInfo) argExprs =
    trackErrors {
        let css = CreateCodegenState tcVal g amap
        let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m (DisplayEnv.Empty g)
        let! _res = SolveMemberConstraint csenv true PermitWeakResolution.Yes 0 m NoTrace traitInfo
        return GenWitnessExpr amap g m traitInfo argExprs
    }

/// Generate the lambda argument passed for a use of a generic construct that accepts trait witnesses
let CodegenWitnessesForTyparInst tcVal g amap m typars tyargs =
    trackErrors {
        let css = CreateCodegenState tcVal g amap
        let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m (DisplayEnv.Empty g)
        let ftps, _renaming, tinst = FreshenTypeInst g m typars
        let traitInfos = GetTraitConstraintInfosOfTypars g ftps
        let! _res = SolveTyparsEqualTypes csenv 0 m NoTrace tinst tyargs
        return GenWitnessArgs amap g m traitInfos
    }

/// Generate the lambda argument passed for a use of a generic construct that accepts trait witnesses
let CodegenWitnessArgForTraitConstraint tcVal g amap m traitInfo =
    trackErrors {
        let css = CreateCodegenState tcVal g amap
        let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m (DisplayEnv.Empty g)
        let! _res = SolveMemberConstraint csenv true PermitWeakResolution.Yes 0 m NoTrace traitInfo
        return GenWitnessExprLambda amap g m traitInfo
    }

/// For some code like "let f() = ([] = [])", a free choice is made for a type parameter
/// for an interior type variable.  This chooses a solution for a type parameter subject
/// to its constraints and applies that solution by using a constraint.
let ChooseTyparSolutionAndSolve css denv tp =
    let g = css.g
    let amap = css.amap
    let max, m = ChooseTyparSolutionAndRange g amap tp
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    PostponeOnFailedMemberConstraintResolution csenv NoTrace
        (fun csenv -> SolveTyparEqualsType csenv 0 m NoTrace (mkTyparTy tp) max)
        (fun err -> ErrorD(ErrorFromApplyingDefault(g, denv, tp, max, err, m)))
    |> RaiseOperationResult

let CheckDeclaredTypars denv css m typars1 typars2 = 
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    PostponeOnFailedMemberConstraintResolution csenv NoTrace
        (fun csenv -> 
            CollectThenUndo (fun newTrace -> 
               SolveTypeEqualsTypeEqns csenv 0 m (WithTrace newTrace) None
                   (List.map mkTyparTy typars1) 
                   (List.map mkTyparTy typars2)))
        (fun res ->
            ErrorD (ErrorFromAddingConstraint(denv, res, m)))
    |> RaiseOperationResult

let CanonicalizePartialInferenceProblem css denv m tps =
    // Canonicalize constraints prior to generalization 
    let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m denv
    let csenv = { csenv with ErrorOnFailedMemberConstraintResolution = true }
    IgnoreFailedMemberConstraintResolution
        (fun () -> CanonicalizeRelevantMemberConstraints csenv 0 NoTrace tps)
        (fun res -> ErrorD (ErrorFromAddingConstraint(denv, res, m))) 
    |> RaiseOperationResult

/// An approximation used during name resolution for intellisense to eliminate extension members which will not
/// apply to a particular object argument. This is given as the isApplicableMeth argument to the partial name resolution
/// functions in nameres.fs.
let IsApplicableMethApprox g amap m (minfo: MethInfo) availObjTy = 
    // Prepare an instance of a constraint solver
    // If it's an instance method, then try to match the object argument against the required object argument
    if minfo.IsExtensionMember then 
        let css = 
            { g = g
              amap = amap
              TcVal = (fun _ -> failwith "should not be called")
              ExtraCxs = HashMultiMap(10, HashIdentity.Structural)
              InfoReader = InfoReader(g, amap)
              PostInferenceChecksPreDefaults = ResizeArray() 
              PostInferenceChecksFinal = ResizeArray()
              WarnWhenUsingWithoutNullOnAWithNullTarget = None}
        let csenv = MakeConstraintSolverEnv ContextInfo.NoContext css m (DisplayEnv.Empty g)
        let minst = FreshenMethInfo m minfo
        match minfo.GetObjArgTypes(amap, m, minst) with
        | [reqdObjTy] -> 
            let reqdObjTy = if isByrefTy g reqdObjTy then destByrefTy g reqdObjTy else reqdObjTy // This is to support byref extension methods.
            TryD (fun () ->
                    trackErrors {
                        do! SolveTypeSubsumesType csenv 0 m NoTrace None reqdObjTy availObjTy
                        return true
                    })
                 (fun _err -> ResultD false)
            |> CommitOperationResult
        | _ -> true
    else
        true