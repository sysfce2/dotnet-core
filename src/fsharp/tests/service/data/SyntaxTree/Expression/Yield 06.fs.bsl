ImplFile
  (ParsedImplFileInput
     ("/root/Expression/Yield 06.fs", false, QualifiedNameOfFile Module, [],
      [SynModuleOrNamespace
         ([Module], false, NamedModule,
          [Expr
             (App
                (NonAtomic, false, Ident seq,
                 ComputationExpr
                   (false,
                    YieldOrReturn
                      ((true, false),
                       Typed
                         (ArrayOrListComputed
                            (false,
                             IndexRange
                               (Some (Const (Int32 1, (4,12--4,13))),
                                (4,14--4,16),
                                Some (Const (Int32 10, (4,17--4,19))),
                                (4,12--4,13), (4,17--4,19), (4,12--4,19)),
                             (4,10--4,21)), FromParseError (4,23--4,23),
                          (4,10--4,23)), (4,4--4,23),
                       { YieldOrReturnKeyword = (4,4--4,9) }), (3,4--5,1)),
                 (3,0--5,1)), (3,0--5,1))],
          PreXmlDoc ((1,0), FSharp.Compiler.Xml.XmlDocCollector), [], None,
          (1,0--5,1), { LeadingKeyword = Module (1,0--1,6) })], (true, true),
      { ConditionalDirectives = []
        WarnDirectives = []
        CodeComments = [] }, set []))

(5,0)-(5,1) parse error Unexpected symbol '}' in expression
(4,23)-(4,23) parse error Expecting type
