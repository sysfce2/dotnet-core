ImplFile
  (ParsedImplFileInput
     ("/root/SynType/Typed LetBang AndBang 02.fs", false,
      QualifiedNameOfFile Module, [],
      [SynModuleOrNamespace
         ([Module], false, NamedModule,
          [Expr
             (App
                (NonAtomic, false, Ident async,
                 ComputationExpr
                   (false,
                    LetOrUseBang
                      (Yes (4,4--4,40), false, true,
                       Paren
                         (Typed
                            (Named
                               (SynIdent (res, None), false, None, (4,10--4,13)),
                             LongIdent (SynLongIdent ([int], [], [None])),
                             (4,10--4,18)), (4,9--4,19)),
                       App
                         (NonAtomic, false, Ident async,
                          ComputationExpr
                            (false,
                             YieldOrReturn
                               ((false, true), Const (Int32 1, (4,37--4,38)),
                                (4,30--4,38),
                                { YieldOrReturnKeyword = (4,30--4,36) }),
                             (4,28--4,40)), (4,22--4,40)),
                       [SynExprAndBang
                          (Yes (5,4--5,41), false, true,
                           Paren
                             (Typed
                                (Named
                                   (SynIdent (res2, None), false, None,
                                    (5,10--5,14)),
                                 LongIdent (SynLongIdent ([int], [], [None])),
                                 (5,10--5,19)), (5,9--5,20)),
                           App
                             (NonAtomic, false, Ident async,
                              ComputationExpr
                                (false,
                                 YieldOrReturn
                                   ((false, true), Const (Int32 2, (5,38--5,39)),
                                    (5,31--5,39),
                                    { YieldOrReturnKeyword = (5,31--5,37) }),
                                 (5,29--5,41)), (5,23--5,41)), (5,4--5,41),
                           { AndBangKeyword = (5,4--5,8)
                             EqualsRange = (5,21--5,22)
                             InKeyword = None })],
                       YieldOrReturn
                         ((false, true), Ident res, (6,4--6,14),
                          { YieldOrReturnKeyword = (6,4--6,10) }), (4,4--6,14),
                       { LetOrUseBangKeyword = (4,4--4,8)
                         EqualsRange = Some (4,20--4,21) }), (3,6--7,1)),
                 (3,0--7,1)), (3,0--7,1))],
          PreXmlDoc ((1,0), FSharp.Compiler.Xml.XmlDocCollector), [], None,
          (1,0--7,1), { LeadingKeyword = Module (1,0--1,6) })], (true, true),
      { ConditionalDirectives = []
        WarnDirectives = []
        CodeComments = [] }, set []))
