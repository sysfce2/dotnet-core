// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

#nullable disable

namespace Microsoft.Build.CommandLine
{
    /// <summary>
    /// This class encapsulates the switches gathered from the application command line. It helps with switch detection, parameter
    /// accumulation, and error generation.
    /// </summary>
    internal sealed class CommandLineSwitches
    {
        /// <summary>
        /// Enumeration of all recognized switches that do not take any parameters.
        /// </summary>
        /// <remarks>
        /// WARNING: the values of this enumeration are also used to index/size arrays, and thus the following rules apply:
        /// 1) the first valid switch must have a value/index of 0
        /// 2) the value of the last member of the enumeration must indicate the number of valid switches
        /// 3) the values of the first and last members of the enumeration are invalid array indices
        /// </remarks>
        internal enum ParameterlessSwitch
        {
            Invalid = -1,
            Help = 0,
            Version,
            NoLogo,
            NoAutoResponse,
            NoConsoleLogger,
            FileLogger,
            FileLogger1,
            FileLogger2,
            FileLogger3,
            FileLogger4,
            FileLogger5,
            FileLogger6,
            FileLogger7,
            FileLogger8,
            FileLogger9,
            DistributedFileLogger,
#if DEBUG
            WaitForDebugger,
#endif
            // This has to be kept as last enum value
            NumberOfParameterlessSwitches
        }

        /// <summary>
        /// Enumeration of all recognized switches that take/require parameters.
        /// </summary>
        /// <remarks>
        /// WARNING: the values of this enumeration are also used to index/size arrays, and thus the following rules apply:
        /// 1) the first valid switch must have a value/index of 0
        /// 2) the value of the last member of the enumeration must indicate the number of valid switches
        /// 3) the values of the first and last members of the enumeration are invalid array indices
        /// </remarks>
        internal enum ParameterizedSwitch
        {
            Invalid = -1,
            Project = 0,
            Target,
            Property,
            Logger,
            DistributedLogger,
            Verbosity,
#if FEATURE_XML_SCHEMA_VALIDATION
            Validate,
#endif
            ConsoleLoggerParameters,
            NodeMode,
            MaxCPUCount,
            IgnoreProjectExtensions,
            ToolsVersion,
            FileLoggerParameters,
            FileLoggerParameters1,
            FileLoggerParameters2,
            FileLoggerParameters3,
            FileLoggerParameters4,
            FileLoggerParameters5,
            FileLoggerParameters6,
            FileLoggerParameters7,
            FileLoggerParameters8,
            FileLoggerParameters9,
            TerminalLogger,
            TerminalLoggerParameters,
            NodeReuse,
            Preprocess,
            Targets,
            WarningsAsErrors,
            WarningsNotAsErrors,
            WarningsAsMessages,
            BinaryLogger,
            Check,
            Restore,
            ProfileEvaluation,
            RestoreProperty,
            Interactive,
            IsolateProjects,
            GraphBuild,
            InputResultsCaches,
            OutputResultsCache,
#if FEATURE_REPORTFILEACCESSES
            ReportFileAccesses,
#endif
            LowPriority,
            Question,
            DetailedSummary,
            GetProperty,
            GetItem,
            GetTargetResult,
            GetResultOutputFile,
            FeatureAvailability,
            MultiThreaded,
            // This has to be kept as last enum value
            NumberOfParameterizedSwitches,
        }

        /// <summary>
        /// This struct packages the information required to identify a switch that doesn't take any parameters. It is used when
        /// parsing the command line.
        /// </summary>
        private struct ParameterlessSwitchInfo
        {
            /// <summary>
            /// Initializes struct data.
            /// </summary>
            /// <param name="switchNames"></param>
            /// <param name="parameterlessSwitch"></param>
            /// <param name="duplicateSwitchErrorMessage"></param>
            internal ParameterlessSwitchInfo(
                string[] switchNames,
                ParameterlessSwitch parameterlessSwitch,
                string duplicateSwitchErrorMessage,
                string resourceId)
            {
                this.switchNames = switchNames;
                this.duplicateSwitchErrorMessage = duplicateSwitchErrorMessage;
                this.parameterlessSwitch = parameterlessSwitch;
                this.resourceId = resourceId;
            }

            // names of the switch (without leading switch indicator)
            internal string[] switchNames;
            // if null, indicates that switch is allowed to appear multiple times on the command line; otherwise, holds the error
            // message to display if switch appears more than once
            internal string duplicateSwitchErrorMessage;
            // the switch id
            internal ParameterlessSwitch parameterlessSwitch;
            internal string resourceId;
        }

        /// <summary>
        /// This struct packages the information required to identify a switch that takes parameters. It is used when parsing the
        /// command line.
        /// </summary>
        private struct ParameterizedSwitchInfo
        {
            /// <summary>
            /// Initializes struct data.
            /// </summary>
            /// <param name="switchNames">Names of the switch (without leading switch indicator).</param>
            /// <param name="parameterizedSwitch">The switch id.</param>
            /// <param name="duplicateSwitchErrorMessage">If <see langword="null"/>, indicates that switch is allowed to appear multiple times on the command line; otherwise, holds the error message to display if switch appears more than once</param>
            /// <param name="multipleParametersAllowed">Indicates if switch can take multiple parameters (equivalent to switch appearing multiple times on command line)
            /// NOTE: for most switches, if a switch is allowed to appear multiple times on the command line, then multiple
            /// parameters can be provided per switch; however, some switches cannot take multiple parameters.</param>
            /// <param name="missingParametersErrorMessage">If <see langword="null"/>, indicates that switch is allowed to have no parameters; otherwise, holds the error message to show if switch is found without parameters on the command line</param>
            /// <param name="unquoteParameters">Indicates if quotes should be removed from the switch parameters</param>
            /// <param name="emptyParametersAllowed">Indicates if empty parameters are allowed and if so an empty string will be added to the list of parameter values.</param>
            internal ParameterizedSwitchInfo(
                string[] switchNames,
                ParameterizedSwitch parameterizedSwitch,
                string duplicateSwitchErrorMessage,
                bool multipleParametersAllowed,
                string missingParametersErrorMessage,
                bool unquoteParameters,
                bool emptyParametersAllowed,
                string resourceId)
            {
                this.switchNames = switchNames;
                this.duplicateSwitchErrorMessage = duplicateSwitchErrorMessage;
                this.multipleParametersAllowed = multipleParametersAllowed;
                this.missingParametersErrorMessage = missingParametersErrorMessage;
                this.unquoteParameters = unquoteParameters;
                this.parameterizedSwitch = parameterizedSwitch;
                this.emptyParametersAllowed = emptyParametersAllowed;
                this.resourceId = resourceId;
            }

            internal string[] switchNames;
            internal string duplicateSwitchErrorMessage;
            internal bool multipleParametersAllowed;
            internal string missingParametersErrorMessage;
            internal bool unquoteParameters;
            internal ParameterizedSwitch parameterizedSwitch;
            internal bool emptyParametersAllowed;
            internal string resourceId;
        }

        // map switches that do not take parameters to their identifiers (taken from ParameterlessSwitch enum)
        // WARNING: keep this map in the same order as the ParameterlessSwitch enumeration
        private static readonly ParameterlessSwitchInfo[] s_parameterlessSwitchesMap =
        {
            //----------------------------------------------------------------------------------------------------------------------------------------------------------
            //                                          Switch Names                        Switch Id                             Dup Error  Light up key    resourceId
            //----------------------------------------------------------------------------------------------------------------------------------------------------------
            new ParameterlessSwitchInfo(  ["help", "h", "?"],                        ParameterlessSwitch.Help,                  null,       "HelpMessage_4_HelpSwitch"),
            new ParameterlessSwitchInfo(  ["version", "ver"],                        ParameterlessSwitch.Version,               null,       "HelpMessage_6_VersionSwitch"),
            new ParameterlessSwitchInfo(  ["nologo"],                                ParameterlessSwitch.NoLogo,                null,       "HelpMessage_5_NoLogoSwitch"),
            new ParameterlessSwitchInfo(  ["noautoresponse", "noautorsp"],           ParameterlessSwitch.NoAutoResponse,        null,       "HelpMessage_8_NoAutoResponseSwitch"),
            new ParameterlessSwitchInfo(  ["noconsolelogger", "noconlog"],           ParameterlessSwitch.NoConsoleLogger,       null,       "HelpMessage_14_NoConsoleLoggerSwitch"),
            new ParameterlessSwitchInfo(  ["filelogger", "fl"],                      ParameterlessSwitch.FileLogger,            null,       "HelpMessage_20_FileLoggerSwitch"),
            new ParameterlessSwitchInfo(  ["filelogger1", "fl1"],                    ParameterlessSwitch.FileLogger1,           null,       "HelpMessage_20_FileLoggerSwitch"),
            new ParameterlessSwitchInfo(  ["filelogger2", "fl2"],                    ParameterlessSwitch.FileLogger2,           null,       "HelpMessage_20_FileLoggerSwitch"),
            new ParameterlessSwitchInfo(  ["filelogger3", "fl3"],                    ParameterlessSwitch.FileLogger3,           null,       "HelpMessage_20_FileLoggerSwitch"),
            new ParameterlessSwitchInfo(  ["filelogger4", "fl4"],                    ParameterlessSwitch.FileLogger4,           null,       "HelpMessage_20_FileLoggerSwitch"),
            new ParameterlessSwitchInfo(  ["filelogger5", "fl5"],                    ParameterlessSwitch.FileLogger5,           null,       "HelpMessage_20_FileLoggerSwitch"),
            new ParameterlessSwitchInfo(  ["filelogger6", "fl6"],                    ParameterlessSwitch.FileLogger6,           null,       "HelpMessage_20_FileLoggerSwitch"),
            new ParameterlessSwitchInfo(  ["filelogger7", "fl7"],                    ParameterlessSwitch.FileLogger7,           null,       "HelpMessage_20_FileLoggerSwitch"),
            new ParameterlessSwitchInfo(  ["filelogger8", "fl8"],                    ParameterlessSwitch.FileLogger8,           null,       "HelpMessage_20_FileLoggerSwitch"),
            new ParameterlessSwitchInfo(  ["filelogger9", "fl9"],                    ParameterlessSwitch.FileLogger9,           null,       "HelpMessage_20_FileLoggerSwitch"),
            new ParameterlessSwitchInfo(  ["distributedfilelogger", "dfl"],          ParameterlessSwitch.DistributedFileLogger, null,       "HelpMessage_21_DistributedFileLoggerSwitch"),
#if DEBUG
            new ParameterlessSwitchInfo(  ["waitfordebugger", "wfd"],                ParameterlessSwitch.WaitForDebugger,       null,       null),
#endif
        };

        // map switches that take parameters to their identifiers (taken from ParameterizedSwitch enum)
        // WARNING: keep this map in the same order as the ParameterizedSwitch enumeration
        private static readonly ParameterizedSwitchInfo[] s_parameterizedSwitchesMap =
        {
            //-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
            //                                          Switch Names                            Switch Id                                       Duplicate Switch Error          Multi Params?   Missing Parameters Error           Unquote?    Empty?   reosurceId
            //-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
            new ParameterizedSwitchInfo(  [null],                                ParameterizedSwitch.Project,                    "DuplicateProjectSwitchError",  false,          null,                                  true,   false,   null),
            new ParameterizedSwitchInfo(  ["target", "t"],                       ParameterizedSwitch.Target,                     null,                           true,           "MissingTargetError",                  true,   false,   "HelpMessage_9_TargetSwitch"),
            new ParameterizedSwitchInfo(  ["property", "p"],                    ParameterizedSwitch.Property,                   null,                           true,           "MissingPropertyError",                true,   false,   "HelpMessage_10_PropertySwitch"),
            new ParameterizedSwitchInfo(  ["logger", "l"],                      ParameterizedSwitch.Logger,                     null,                           false,          "MissingLoggerError",                  false,  false,   "HelpMessage_11_LoggerSwitch"),
            new ParameterizedSwitchInfo(  ["distributedlogger", "dl"],          ParameterizedSwitch.DistributedLogger,          null,                           false,          "MissingLoggerError",                  false,  false,   "HelpMessage_18_DistributedLoggerSwitch"),
            new ParameterizedSwitchInfo(  ["verbosity", "v"],                   ParameterizedSwitch.Verbosity,                  null,                           false,          "MissingVerbosityError",               true,   false,   "HelpMessage_12_VerbositySwitch"),
#if FEATURE_XML_SCHEMA_VALIDATION
            new ParameterizedSwitchInfo(  ["validate", "val"],                  ParameterizedSwitch.Validate,                   null,                           false,          null,                                  true,   false,   "HelpMessage_15_ValidateSwitch"),
#endif
            new ParameterizedSwitchInfo(  ["consoleloggerparameters", "clp"],   ParameterizedSwitch.ConsoleLoggerParameters,    null,                           false,          "MissingConsoleLoggerParameterError",  true,   false,   "HelpMessage_13_ConsoleLoggerParametersSwitch"),
            new ParameterizedSwitchInfo(  ["nodemode", "nmode"],                ParameterizedSwitch.NodeMode,                   null,                           false,          null,                                  false,  false,   null),
            new ParameterizedSwitchInfo(  ["maxcpucount", "m"],                 ParameterizedSwitch.MaxCPUCount,                null,                           false,          "MissingMaxCPUCountError",             true,   false,   "HelpMessage_17_MaximumCPUSwitch"),
            new ParameterizedSwitchInfo(  ["ignoreprojectextensions", "ignore"], ParameterizedSwitch.IgnoreProjectExtensions,    null,                           true,           "MissingIgnoreProjectExtensionsError", true,   false,   "HelpMessage_19_IgnoreProjectExtensionsSwitch"),
            new ParameterizedSwitchInfo(  ["toolsversion","tv"],                ParameterizedSwitch.ToolsVersion,               null,                           false,          "MissingToolsVersionError",            true,   false,   "HelpMessage_23_ToolsVersionSwitch"),
            new ParameterizedSwitchInfo(  ["fileloggerparameters", "flp"],      ParameterizedSwitch.FileLoggerParameters,       null,                           false,          "MissingFileLoggerParameterError",     true,   false,   "HelpMessage_22_FileLoggerParametersSwitch"),
            new ParameterizedSwitchInfo(  ["fileloggerparameters1", "flp1"],    ParameterizedSwitch.FileLoggerParameters1,      null,                           false,          "MissingFileLoggerParameterError",     true,   false,   "HelpMessage_22_FileLoggerParametersSwitch"),
            new ParameterizedSwitchInfo(  ["fileloggerparameters2", "flp2"],    ParameterizedSwitch.FileLoggerParameters2,      null,                           false,          "MissingFileLoggerParameterError",     true,   false,   "HelpMessage_22_FileLoggerParametersSwitch"),
            new ParameterizedSwitchInfo(  ["fileloggerparameters3", "flp3"],    ParameterizedSwitch.FileLoggerParameters3,      null,                           false,          "MissingFileLoggerParameterError",     true,   false,   "HelpMessage_22_FileLoggerParametersSwitch"),
            new ParameterizedSwitchInfo(  ["fileloggerparameters4", "flp4"],    ParameterizedSwitch.FileLoggerParameters4,      null,                           false,          "MissingFileLoggerParameterError",     true,   false,   "HelpMessage_22_FileLoggerParametersSwitch"),
            new ParameterizedSwitchInfo(  ["fileloggerparameters5", "flp5"],    ParameterizedSwitch.FileLoggerParameters5,      null,                           false,          "MissingFileLoggerParameterError",     true,   false,   "HelpMessage_22_FileLoggerParametersSwitch"),
            new ParameterizedSwitchInfo(  ["fileloggerparameters6", "flp6"],    ParameterizedSwitch.FileLoggerParameters6,      null,                           false,          "MissingFileLoggerParameterError",     true,   false,   "HelpMessage_22_FileLoggerParametersSwitch"),
            new ParameterizedSwitchInfo(  ["fileloggerparameters7", "flp7"],    ParameterizedSwitch.FileLoggerParameters7,      null,                           false,          "MissingFileLoggerParameterError",     true,   false,   "HelpMessage_22_FileLoggerParametersSwitch"),
            new ParameterizedSwitchInfo(  ["fileloggerparameters8", "flp8"],    ParameterizedSwitch.FileLoggerParameters8,      null,                           false,          "MissingFileLoggerParameterError",     true,   false,   "HelpMessage_22_FileLoggerParametersSwitch"),
            new ParameterizedSwitchInfo(  ["fileloggerparameters9", "flp9"],    ParameterizedSwitch.FileLoggerParameters9,      null,                           false,          "MissingFileLoggerParameterError",     true,   false,   "HelpMessage_22_FileLoggerParametersSwitch"),
            // To not break existing use, keep supporting live logger switches
            new ParameterizedSwitchInfo(  ["livelogger", "ll", "terminallogger", "tl"], ParameterizedSwitch.TerminalLogger,      null,                           true,           null,                                  true,   true,    "HelpMessage_47_TerminalLoggerSwitch"),
            new ParameterizedSwitchInfo(  ["terminalloggerparameters", "tlp"],  ParameterizedSwitch.TerminalLoggerParameters,   null,                           false,          "MissingTerminalLoggerParameterError", true,   false,   "HelpMessage_48_TerminalLoggerParametersSwitch"),
#if FEATURE_NODE_REUSE
            new ParameterizedSwitchInfo(  ["nodereuse", "nr"],                  ParameterizedSwitch.NodeReuse,                  null,                           false,          "MissingNodeReuseParameterError",      true,   false,   "HelpMessage_24_NodeReuse"),
#endif
            new ParameterizedSwitchInfo(  ["preprocess", "pp"],                 ParameterizedSwitch.Preprocess,                 null,                           false,          null,                                  true,   false,   "HelpMessage_25_PreprocessSwitch"),
            new ParameterizedSwitchInfo(  ["targets", "ts"],                    ParameterizedSwitch.Targets,                    null,                           false,          null,                                  true,   false,   "HelpMessage_38_TargetsSwitch"),
            new ParameterizedSwitchInfo(  ["warnaserror", "err"],               ParameterizedSwitch.WarningsAsErrors,           null,                           true,           null,                                  true,   true,    "HelpMessage_28_WarnAsErrorSwitch"),
            new ParameterizedSwitchInfo(  ["warnnotaserror", "noerr"],          ParameterizedSwitch.WarningsNotAsErrors,        null,                           true,           "MissingWarnNotAsErrorParameterError", true,   false,   "HelpMessage_40_WarnNotAsErrorSwitch"),
            new ParameterizedSwitchInfo(  ["warnasmessage", "nowarn"],          ParameterizedSwitch.WarningsAsMessages,         null,                           true,           "MissingWarnAsMessageParameterError",  true,   false,   "HelpMessage_29_WarnAsMessageSwitch"),
            new ParameterizedSwitchInfo(  ["binarylogger", "bl"],               ParameterizedSwitch.BinaryLogger,               null,                           false,          null,                                  true,   false,   "HelpMessage_30_BinaryLoggerSwitch"),
            new ParameterizedSwitchInfo(  ["check"],                             ParameterizedSwitch.Check,                      null,                           false,          null,                                  true,   false,   "HelpMessage_52_BuildCheckSwitch"),
            new ParameterizedSwitchInfo(  ["restore", "r"],                     ParameterizedSwitch.Restore,                    null,                           false,          null,                                  true,   false,   "HelpMessage_31_RestoreSwitch"),
            new ParameterizedSwitchInfo(  ["profileevaluation", "prof"],        ParameterizedSwitch.ProfileEvaluation,          null,                           false,          "MissingProfileParameterError",        true,   false,   "HelpMessage_32_ProfilerSwitch"),
            new ParameterizedSwitchInfo(  ["restoreproperty", "rp"],            ParameterizedSwitch.RestoreProperty,            null,                           true,           "MissingPropertyError",                true,   false,   "HelpMessage_33_RestorePropertySwitch"),
            new ParameterizedSwitchInfo(  ["interactive"],                      ParameterizedSwitch.Interactive,                null,                           false,          null,                                  true,   false,   "HelpMessage_34_InteractiveSwitch"),
            new ParameterizedSwitchInfo(  ["isolateprojects", "isolate"],       ParameterizedSwitch.IsolateProjects,            null,                           false,          null,                                  true,   false,   "HelpMessage_35_IsolateProjectsSwitch"),
            new ParameterizedSwitchInfo(  ["graphbuild", "graph"],              ParameterizedSwitch.GraphBuild,                 null,                           true,           null,                                  true,   false,   "HelpMessage_36_GraphBuildSwitch"),
            new ParameterizedSwitchInfo(  ["inputResultsCaches", "irc"],        ParameterizedSwitch.InputResultsCaches,         null,                           true,           null,                                  true,   true,    "HelpMessage_InputCachesFiles"),
            new ParameterizedSwitchInfo(  ["outputResultsCache", "orc"],        ParameterizedSwitch.OutputResultsCache,         "DuplicateOutputResultsCache",  false,          null,                                  true,   true,    "HelpMessage_OutputCacheFile"),
#if FEATURE_REPORTFILEACCESSES
            new ParameterizedSwitchInfo(  ["reportfileaccesses"],               ParameterizedSwitch.ReportFileAccesses,         null,                           false,          null,                                  true,   false,   "HelpMessage_42_ReportFileAccessesSwitch"),
#endif
            new ParameterizedSwitchInfo(  ["lowpriority", "low"],               ParameterizedSwitch.LowPriority,                null,                           false,          null,                                  true,   false,   "HelpMessage_39_LowPrioritySwitch"),
            new ParameterizedSwitchInfo(  ["question", "q"],                    ParameterizedSwitch.Question,                   null,                           false,          null,                                  true,   false,   "HelpMessage_41_QuestionSwitch"),
            new ParameterizedSwitchInfo(  ["detailedsummary", "ds"],            ParameterizedSwitch.DetailedSummary,            null,                           false,          null,                                  true,   false,   "HelpMessage_26_DetailedSummarySwitch"),
            new ParameterizedSwitchInfo(  ["getProperty"],                      ParameterizedSwitch.GetProperty,                null,                           true,           "MissingGetPropertyError",             true,   false,   "HelpMessage_43_GetPropertySwitch"),
            new ParameterizedSwitchInfo(  ["getItem"],                          ParameterizedSwitch.GetItem,                    null,                           true,           "MissingGetItemError",                 true,   false,   "HelpMessage_44_GetItemSwitch"),
            new ParameterizedSwitchInfo(  ["getTargetResult"],                  ParameterizedSwitch.GetTargetResult,            null,                           true,           "MissingGetTargetResultError",         true,   false,   "HelpMessage_45_GetTargetResultSwitch"),
            new ParameterizedSwitchInfo(  ["getResultOutputFile"],              ParameterizedSwitch.GetResultOutputFile,        null,                           true,           "MissingGetResultFileError",           true,   false,   "HelpMessage_51_GetResultOutputFileSwitch"),
            new ParameterizedSwitchInfo(  ["featureAvailability", "fa"],        ParameterizedSwitch.FeatureAvailability,        null,                           true,           "MissingFeatureAvailabilityError",     true,   false,   "HelpMessage_46_FeatureAvailabilitySwitch"),
            new ParameterizedSwitchInfo(  ["multithreaded", "mt"],              ParameterizedSwitch.MultiThreaded,              null,                           false,          null,                                  true,   false,   "HelpMessage_49_MultiThreadedSwitch")
        };

        /// <summary>
        /// Identifies/detects a switch that takes no parameters.
        /// </summary>
        /// <param name="switchName"></param>
        /// <param name="parameterlessSwitch">[out] switch identifier (from ParameterlessSwitch enumeration)</param>
        /// <param name="duplicateSwitchErrorMessage"></param>
        /// <returns>true, if switch is a recognized switch that doesn't take parameters</returns>
        internal static bool IsParameterlessSwitch(
            string switchName,
            out ParameterlessSwitch parameterlessSwitch,
            out string duplicateSwitchErrorMessage)
        {
            parameterlessSwitch = ParameterlessSwitch.Invalid;
            duplicateSwitchErrorMessage = null;

            foreach (ParameterlessSwitchInfo switchInfo in s_parameterlessSwitchesMap)
            {
                foreach (string parameterlessSwitchName in switchInfo.switchNames)
                {
                    if (string.Equals(switchName, parameterlessSwitchName, StringComparison.OrdinalIgnoreCase))
                    {
                        parameterlessSwitch = switchInfo.parameterlessSwitch;
                        duplicateSwitchErrorMessage = switchInfo.duplicateSwitchErrorMessage;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Identifies/detects a switch that takes parameters.
        /// </summary>
        /// <param name="switchName"></param>
        /// <param name="parameterizedSwitch">[out] switch identifier (from ParameterizedSwitch enumeration)</param>
        /// <param name="duplicateSwitchErrorMessage"></param>
        /// <param name="multipleParametersAllowed"></param>
        /// <param name="missingParametersErrorMessage"></param>
        /// <param name="unquoteParameters"></param>
        /// <returns>true, if switch is a recognized switch that takes parameters</returns>
        internal static bool IsParameterizedSwitch(
            string switchName,
            out ParameterizedSwitch parameterizedSwitch,
            out string duplicateSwitchErrorMessage,
            out bool multipleParametersAllowed,
            out string missingParametersErrorMessage,
            out bool unquoteParameters,
            out bool emptyParametersAllowed)
        {
            parameterizedSwitch = ParameterizedSwitch.Invalid;
            duplicateSwitchErrorMessage = null;
            multipleParametersAllowed = false;
            missingParametersErrorMessage = null;
            unquoteParameters = false;
            emptyParametersAllowed = false;

            foreach (ParameterizedSwitchInfo switchInfo in s_parameterizedSwitchesMap)
            {
                foreach (string parameterizedSwitchName in switchInfo.switchNames)
                {
                    if (string.Equals(switchName, parameterizedSwitchName, StringComparison.OrdinalIgnoreCase))
                    {
                        parameterizedSwitch = switchInfo.parameterizedSwitch;
                        duplicateSwitchErrorMessage = switchInfo.duplicateSwitchErrorMessage;
                        multipleParametersAllowed = switchInfo.multipleParametersAllowed;
                        missingParametersErrorMessage = switchInfo.missingParametersErrorMessage;
                        unquoteParameters = switchInfo.unquoteParameters;
                        emptyParametersAllowed = switchInfo.emptyParametersAllowed;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Get the distinct parameterized switchs map resource ids.
        /// </summary>
        /// <returns>Parameterized switches map resource ids.</returns>
        internal static IEnumerable<string> GetParameterizedSwitchResourceIds()
        {
            HashSet<string> parameterizedSwitchResourceIds = new HashSet<string>();
            foreach (ParameterizedSwitchInfo parameterizedSwitch in s_parameterizedSwitchesMap)
            {
                string resourceId = parameterizedSwitch.resourceId;
                if (!string.IsNullOrEmpty(resourceId) && parameterizedSwitchResourceIds.Add(resourceId))
                {
                    yield return resourceId;
                }
            }
        }

        /// <summary>
        /// Get the distinct parameterless switchs map resource ids.
        /// </summary>
        /// <returns>Parameterless switchs map resource ids</returns>
        internal static IEnumerable<string> GetParameterlessSwitchResourceIds()
        {
            HashSet<string> parameterlessSwitchResourceIds = new HashSet<string>();
            foreach (ParameterlessSwitchInfo parameterlessSwitch in s_parameterlessSwitchesMap)
            {
                string resourceId = parameterlessSwitch.resourceId;
                if (parameterlessSwitchResourceIds.Add(resourceId) && !string.IsNullOrEmpty(resourceId))
                {
                    yield return resourceId;
                }
            }
        }

        /// <summary>
        /// This struct stores the details of a switch that doesn't take parameters that is detected on the command line.
        /// </summary>
        private struct DetectedParameterlessSwitch
        {
            // the actual text of the switch
            internal string commandLineArg;
        }

        /// <summary>
        /// This struct stores the details of a switch that takes parameters that is detected on the command line.
        /// </summary>
        private struct DetectedParameterizedSwitch
        {
            // the actual text of the switch
            internal string commandLineArg;

            // the parsed switch parameters
            internal ArrayList parameters;
        }

        // for each recognized switch that doesn't take parameters, this array indicates if the switch has been detected on the
        // command line
        private DetectedParameterlessSwitch[] _parameterlessSwitches;
        // for each recognized switch that takes parameters, this array indicates if the switch has been detected on the command
        // line, and it provides a store for the switch parameters
        private DetectedParameterizedSwitch[] _parameterizedSwitches;
        // NOTE: the above arrays are instance members because this class is not required to be a singleton

        internal static List<(string path, string contents)> SwitchesFromResponseFiles = new();

        /// <summary>
        /// Default constructor.
        /// </summary>
        internal CommandLineSwitches()
        {
#if DEBUG
            Debug.Assert(s_parameterlessSwitchesMap.Length == (int)ParameterlessSwitch.NumberOfParameterlessSwitches,
                "The map of parameterless switches must have an entry for each switch in the ParameterlessSwitch enumeration.");
            Debug.Assert(s_parameterizedSwitchesMap.Length == (int)ParameterizedSwitch.NumberOfParameterizedSwitches,
                "The map of parameterized switches must have an entry for each switch in the ParameterizedSwitch enumeration.");

            for (int i = 0; i < s_parameterlessSwitchesMap.Length; i++)
            {
                Debug.Assert(i == (int)(s_parameterlessSwitchesMap[i].parameterlessSwitch),
                    "The map of parameterless switches must be ordered the same way as the ParameterlessSwitch enumeration.");
                if (s_parameterlessSwitchesMap[i].parameterlessSwitch is not ParameterlessSwitch.WaitForDebugger)
                {
                    Debug.Assert(!string.IsNullOrEmpty(s_parameterlessSwitchesMap[i].resourceId), "All parameterless switches should be cross-checked against the help message strings");
                }
            }

            for (int i = 0; i < s_parameterizedSwitchesMap.Length; i++)
            {
                Debug.Assert(i == (int)(s_parameterizedSwitchesMap[i].parameterizedSwitch),
                    "The map of parameterized switches must be ordered the same way as the ParameterizedSwitch enumeration.");
                if (s_parameterizedSwitchesMap[i].parameterizedSwitch is not ParameterizedSwitch.Project and
                    not ParameterizedSwitch.NodeMode and not ParameterizedSwitch.Check)
                {
                    Debug.Assert(!string.IsNullOrEmpty(s_parameterizedSwitchesMap[i].resourceId), "All parameterized switches should be cross-checked against the help message strings except from project switch");
                }
            }
#endif
            _parameterlessSwitches = new DetectedParameterlessSwitch[(int)ParameterlessSwitch.NumberOfParameterlessSwitches];
            _parameterizedSwitches = new DetectedParameterizedSwitch[(int)ParameterizedSwitch.NumberOfParameterizedSwitches];
        }

        /// <summary>
        /// Called when a recognized switch that doesn't take parameters is detected on the command line.
        /// </summary>
        /// <param name="parameterlessSwitch"></param>
        internal void SetParameterlessSwitch(ParameterlessSwitch parameterlessSwitch, string commandLineArg)
        {
            // save the switch text
            _parameterlessSwitches[(int)parameterlessSwitch].commandLineArg = commandLineArg;
        }

        // list of recognized switch parameter separators -- for switches that take multiple parameters
        private static readonly char[] s_parameterSeparators = { ',', ';' };

        /// <summary>
        /// Called when a recognized switch that takes parameters is detected on the command line.
        /// </summary>
        /// <param name="parameterizedSwitch"></param>
        /// <param name="switchParameters"></param>
        /// <param name="multipleParametersAllowed"></param>
        /// <param name="unquoteParameters"></param>
        /// <returns>true, if the given parameters were successfully stored</returns>
        internal bool SetParameterizedSwitch(
            ParameterizedSwitch parameterizedSwitch,
            string commandLineArg,
            string switchParameters,
            bool multipleParametersAllowed,
            bool unquoteParameters,
            bool emptyParametersAllowed)
        {
            bool parametersStored = false;

            // if this is the first time this switch has been detected
            if (_parameterizedSwitches[(int)parameterizedSwitch].commandLineArg == null)
            {
                // initialize its parameter storage
                _parameterizedSwitches[(int)parameterizedSwitch].parameters = new ArrayList();

                // save the switch text
                _parameterizedSwitches[(int)parameterizedSwitch].commandLineArg = commandLineArg;
            }
            else
            {
                // append the switch text
                _parameterizedSwitches[(int)parameterizedSwitch].commandLineArg = string.Concat(
                        _parameterizedSwitches[(int)parameterizedSwitch].commandLineArg,
                        " ",
                        commandLineArg);
            }

            // check if the switch has multiple parameters
            if (multipleParametersAllowed)
            {
                if (string.Empty.Equals(switchParameters) && emptyParametersAllowed)
                {
                    // Store a null parameter if its allowed
                    _parameterizedSwitches[(int)parameterizedSwitch].parameters.Add(null);
                    parametersStored = true;
                }
                else
                {
                    if (IsMultipleAllowedSwitchParameterDueToUnquote(unquoteParameters, parameterizedSwitch))
                    {
                        switchParameters = QuotingUtilities.Unquote(switchParameters);
                    }

                    // store all the switch parameters
                    int emptyParameters;
                    _parameterizedSwitches[(int)parameterizedSwitch].parameters.AddRange(QuotingUtilities.SplitUnquoted(switchParameters, int.MaxValue, false /* discard empty parameters */, unquoteParameters, out emptyParameters, s_parameterSeparators));

                    // check if they were all stored successfully i.e. they were all non-empty (after removing quoting, if requested)
                    parametersStored = (emptyParameters == 0);
                }
            }
            else
            {
                if (unquoteParameters)
                {
                    // NOTE: removing quoting from the parameters can reduce the parameters to an empty string
                    switchParameters = QuotingUtilities.Unquote(switchParameters);
                }

                // if the switch actually has parameters, store them
                if (switchParameters.Length > 0)
                {
                    _parameterizedSwitches[(int)parameterizedSwitch].parameters.Add(switchParameters);

                    parametersStored = true;
                }
            }

            return parametersStored;
        }

        /// <summary>
        /// Get the equivalent command line, with response files expanded
        /// and duplicates removed. Prettified, sorted, parameterless first.
        /// Don't include the project file, the caller can put it last.
        /// </summary>
        /// <returns></returns>
        internal string GetEquivalentCommandLineExceptProjectFile()
        {
            var commandLineA = new List<string>();
            var commandLineB = new List<string>();

            for (int i = 0; i < _parameterlessSwitches.Length; i++)
            {
                if (IsParameterlessSwitchSet((ParameterlessSwitch)i))
                {
                    commandLineA.Add(GetParameterlessSwitchCommandLineArg((ParameterlessSwitch)i));
                }
            }

            for (int i = 0; i < _parameterizedSwitches.Length; i++)
            {
                if (IsParameterizedSwitchSet((ParameterizedSwitch)i) && ((ParameterizedSwitch)i != ParameterizedSwitch.Project))
                {
                    commandLineB.Add(GetParameterizedSwitchCommandLineArg((ParameterizedSwitch)i));
                }
            }

            commandLineA.Sort(StringComparer.OrdinalIgnoreCase);
            commandLineB.Sort(StringComparer.OrdinalIgnoreCase);

            return (string.Join(" ", commandLineA).Trim() + " " + string.Join(" ", commandLineB).Trim()).Trim();
        }

        /// <summary>
        /// Indicates if the given switch that doesn't take parameters has already been detected on the command line.
        /// </summary>
        /// <param name="parameterlessSwitch"></param>
        /// <returns>true, if switch has been seen before</returns>
        internal bool IsParameterlessSwitchSet(ParameterlessSwitch parameterlessSwitch)
        {
            return _parameterlessSwitches[(int)parameterlessSwitch].commandLineArg != null;
        }

        /// <summary>
        /// Gets the on/off state on the command line of the given parameterless switch.
        /// </summary>
        /// <remarks>
        /// This indexer is functionally equivalent to IsParameterlessSwitchSet, but semantically very different.
        /// </remarks>
        /// <param name="parameterlessSwitch"></param>
        /// <returns>true if on, false if off</returns>
        internal bool this[ParameterlessSwitch parameterlessSwitch]
        {
            get
            {
                return _parameterlessSwitches[(int)parameterlessSwitch].commandLineArg != null;
            }
        }

        /// <summary>
        /// Gets the command line argument (if any) in which the given parameterless switch was detected.
        /// </summary>
        /// <param name="parameterlessSwitch"></param>
        /// <returns>The switch text, or null if switch was not detected on the command line.</returns>
        internal string GetParameterlessSwitchCommandLineArg(ParameterlessSwitch parameterlessSwitch)
        {
            return _parameterlessSwitches[(int)parameterlessSwitch].commandLineArg;
        }

        /// <summary>
        /// Indicates if the given switch that takes parameters has already been detected on the command line.
        /// </summary>
        /// <remarks>This method is very light-weight.</remarks>
        /// <param name="parameterizedSwitch"></param>
        /// <returns>true, if switch has been seen before</returns>
        internal bool IsParameterizedSwitchSet(ParameterizedSwitch parameterizedSwitch)
        {
            return _parameterizedSwitches[(int)parameterizedSwitch].commandLineArg != null;
        }

        // used to indicate a null parameter list for a switch
        private static readonly string[] s_noParameters = [];

        /// <summary>
        /// Gets the parameters (if any) detected on the command line for the given parameterized switch.
        /// </summary>
        /// <remarks>
        /// WARNING: this indexer is not equivalent to IsParameterizedSwitchSet, and is not light-weight.
        /// </remarks>
        /// <param name="parameterizedSwitch"></param>
        /// <returns>
        /// An array of all the detected parameters for the given switch, or an empty array (NOT null), if the switch has not yet
        /// been detected on the command line.
        /// </returns>
        internal string[] this[ParameterizedSwitch parameterizedSwitch]
        {
            get
            {
                // if switch has not yet been detected
                if (_parameterizedSwitches[(int)parameterizedSwitch].commandLineArg == null)
                {
                    // return an empty parameter list
                    return s_noParameters;
                }
                else
                {
                    // return an array of all detected parameters
                    return (string[])_parameterizedSwitches[(int)parameterizedSwitch].parameters.ToArray(typeof(string));
                }
            }
        }

        /// <summary>
        /// Returns an array containing an array of logger parameters for every file logger enabled on the command line.
        /// If a logger is enabled but no parameters were supplied, the array entry is an empty array.
        /// If a particular logger is not supplied, the array entry is null.
        /// </summary>
        internal string[][] GetFileLoggerParameters()
        {
            string[][] groupedFileLoggerParameters = new string[10][];

            groupedFileLoggerParameters[0] = GetSpecificFileLoggerParameters(ParameterlessSwitch.FileLogger, ParameterizedSwitch.FileLoggerParameters);
            groupedFileLoggerParameters[1] = GetSpecificFileLoggerParameters(ParameterlessSwitch.FileLogger1, ParameterizedSwitch.FileLoggerParameters1);
            groupedFileLoggerParameters[2] = GetSpecificFileLoggerParameters(ParameterlessSwitch.FileLogger2, ParameterizedSwitch.FileLoggerParameters2);
            groupedFileLoggerParameters[3] = GetSpecificFileLoggerParameters(ParameterlessSwitch.FileLogger3, ParameterizedSwitch.FileLoggerParameters3);
            groupedFileLoggerParameters[4] = GetSpecificFileLoggerParameters(ParameterlessSwitch.FileLogger4, ParameterizedSwitch.FileLoggerParameters4);
            groupedFileLoggerParameters[5] = GetSpecificFileLoggerParameters(ParameterlessSwitch.FileLogger5, ParameterizedSwitch.FileLoggerParameters5);
            groupedFileLoggerParameters[6] = GetSpecificFileLoggerParameters(ParameterlessSwitch.FileLogger6, ParameterizedSwitch.FileLoggerParameters6);
            groupedFileLoggerParameters[7] = GetSpecificFileLoggerParameters(ParameterlessSwitch.FileLogger7, ParameterizedSwitch.FileLoggerParameters7);
            groupedFileLoggerParameters[8] = GetSpecificFileLoggerParameters(ParameterlessSwitch.FileLogger8, ParameterizedSwitch.FileLoggerParameters8);
            groupedFileLoggerParameters[9] = GetSpecificFileLoggerParameters(ParameterlessSwitch.FileLogger9, ParameterizedSwitch.FileLoggerParameters9);

            return groupedFileLoggerParameters;
        }

        /// <summary>
        /// Checks if the provided multiple valued parametrized switch needs to be unquoted.
        /// The method will return 'true' in case:
        ///     The changewave 17.10 is not set and
        ///     The parametrized switch is 'Target'
        /// </summary>
        private bool IsMultipleAllowedSwitchParameterDueToUnquote(bool unquoteParameter, ParameterizedSwitch parameterizedSwitch)
        {
            if (!unquoteParameter || !Traits.Instance.EscapeHatches.UnquoteTargetSwitchParameters)
            {
                return false;
            }

            // issue: https://github.com/dotnet/msbuild/issues/9442
            // In order to align the parsing behaviour of Target property when MSBuild invoked from PowerShell or CMD,
            // the target property value will be unquoted before processing further
            if (parameterizedSwitch == ParameterizedSwitch.Target)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// If the specified parameterized switch is set, returns the array of parameters.
        /// Otherwise, if the specified parameterless switch is set, returns an empty array.
        /// Otherwise returns null.
        /// This allows for example "/flp:foo=bar" to imply "/fl".
        /// </summary>
        private string[] GetSpecificFileLoggerParameters(ParameterlessSwitch parameterlessSwitch, ParameterizedSwitch parameterizedSwitch)
        {
            string[] result = null;

            if (IsParameterizedSwitchSet(parameterizedSwitch))
            {
                result = this[parameterizedSwitch];
            }
            else if (IsParameterlessSwitchSet(parameterlessSwitch))
            {
                result = [];
            }

            return result;
        }

        /// <summary>
        /// Gets the command line argument (if any) in which the given parameterized switch was detected.
        /// </summary>
        /// <param name="parameterizedSwitch"></param>
        /// <returns>The switch text, or null if switch was not detected on the command line.</returns>
        internal string GetParameterizedSwitchCommandLineArg(ParameterizedSwitch parameterizedSwitch)
        {
            return _parameterizedSwitches[(int)parameterizedSwitch].commandLineArg;
        }

        /// <summary>
        /// Determines whether any switches have been set in this bag.
        /// </summary>
        /// <returns>Returns true if any switches are set, otherwise false.</returns>
        internal bool HaveAnySwitchesBeenSet()
        {
            for (int i = 0; i < (int)ParameterlessSwitch.NumberOfParameterlessSwitches; i++)
            {
                if (IsParameterlessSwitchSet((ParameterlessSwitch)i))
                {
                    return true;
                }
            }

            for (int j = 0; j < (int)ParameterizedSwitch.NumberOfParameterizedSwitches; j++)
            {
                if (IsParameterizedSwitchSet((ParameterizedSwitch)j))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Called to flag an error when an unrecognized switch is detected on the command line.
        /// </summary>
        /// <param name="badCommandLineArg"></param>
        internal void SetUnknownSwitchError(string badCommandLineArgValue, string commandLine = "")
        {
            SetSwitchError("UnknownSwitchError", badCommandLineArgValue, commandLine);
        }

        /// <summary>
        /// Called to flag an error when a switch that doesn't take parameters is found with parameters on the command line.
        /// </summary>
        /// <param name="badCommandLineArg"></param>
        internal void SetUnexpectedParametersError(string badCommandLineArgValue, string commandLine = "")
        {
            SetSwitchError("UnexpectedParametersError", badCommandLineArgValue, commandLine);
        }

        // information about last flagged error
        // NOTE: these instance members are not initialized unless an error is found
        private string _errorMessage;
        private string _badCommandLineArg;
        private Exception _innerException;
        private bool _isParameterError;
        private string _commandLine;

        /// <summary>
        /// Used to flag/store switch errors.
        /// </summary>
        /// <param name="messageResourceName"></param>
        /// <param name="badCommandLineArg"></param>
        internal void SetSwitchError(string messageResourceNameValue, string badCommandLineArgValue, string commandLine)
        {
            SetError(messageResourceNameValue, badCommandLineArgValue, null, false, commandLine);
        }

        /// <summary>
        /// Used to flag/store parameter errors.
        /// </summary>
        /// <param name="messageResourceName"></param>
        /// <param name="badCommandLineArg"></param>
        internal void SetParameterError(string messageResourceNameValue, string badCommandLineArgValue, string commandLine)
        {
            SetParameterError(messageResourceNameValue, badCommandLineArgValue, null, commandLine);
        }

        /// <summary>
        /// Used to flag/store parameter errors.
        /// </summary>
        /// <param name="messageResourceName"></param>
        /// <param name="badCommandLineArg"></param>
        /// <param name="innerException"></param>
        internal void SetParameterError(string messageResourceNameValue, string badCommandLineArgValue, Exception innerExceptionValue, string commandLine)
        {
            SetError(messageResourceNameValue, badCommandLineArgValue, innerExceptionValue, true, commandLine);
        }

        /// <summary>
        /// Used to flag/store switch and/or parameter errors.
        /// </summary>
        /// <param name="messageResourceName"></param>
        /// <param name="badCommandLineArg"></param>
        /// <param name="innerException"></param>
        /// <param name="isParameterError"></param>
        private void SetError(string messageResourceNameValue, string badCommandLineArgValue, Exception innerExceptionValue, bool isParameterErrorValue, string commandLine)
        {
            if (!HaveErrors())
            {
                _errorMessage = messageResourceNameValue;
                _badCommandLineArg = badCommandLineArgValue;
                _innerException = innerExceptionValue;
                _isParameterError = isParameterErrorValue;
                _commandLine = commandLine;
            }
        }

        /// <summary>
        /// Indicates if any errors were found while parsing the command-line.
        /// </summary>
        /// <returns>true, if any errors were found</returns>
        internal bool HaveErrors()
        {
            return _errorMessage != null;
        }

        /// <summary>
        /// Throws an exception if any errors were found while parsing the command-line.
        /// </summary>
        internal void ThrowErrors()
        {
            if (HaveErrors())
            {
                if (_isParameterError)
                {
                    InitializationException.Throw(_errorMessage, _badCommandLineArg, _innerException, false);
                }
                else
                {
                    StringBuilder sb = StringBuilderCache.Acquire();
                    foreach ((string path, string contents) in SwitchesFromResponseFiles)
                    {
                        sb.Append($"\n{ResourceUtilities.FormatResourceStringStripCodeAndKeyword("ResponseFileSwitchFromLocation", contents, path)}");
                    }
                    CommandLineSwitchException.Throw("SwitchErrorWithArguments", _badCommandLineArg, ResourceUtilities.GetResourceString(_errorMessage), _commandLine, StringBuilderCache.GetStringAndRelease(sb));
                }
            }
        }

        /// <summary>
        /// Appends the given collection of command-line switches to this one.
        /// </summary>
        /// <remarks>
        /// Command-line switches have left-to-right precedence i.e. switches on the right override switches on the left. As a
        /// result, this "append" operation is also performed in a left-to-right manner -- the switches being appended to are
        /// considered to be on the "left", and the switches being appended are on the "right".
        /// </remarks>
        /// <param name="switchesToAppend"></param>
        internal void Append(CommandLineSwitches switchesToAppend, string commandLine = "")
        {
            // if this collection doesn't already have an error registered, but the collection being appended does
            if (!HaveErrors() && switchesToAppend.HaveErrors())
            {
                // register the error from the given collection
                // NOTE: we always store the first error found (parsing left-to-right), and since this collection is considered to
                // be on the "left" of the collection being appended, the error flagged in this collection takes priority over the
                // error in the collection being appended
                _errorMessage = switchesToAppend._errorMessage;
                _badCommandLineArg = switchesToAppend._badCommandLineArg;
                _innerException = switchesToAppend._innerException;
                _isParameterError = switchesToAppend._isParameterError;
                _commandLine = commandLine;
            }

            // NOTE: we might run into some duplicate switch errors below, but if we've already registered the error from the
            // collection being appended, all the duplicate switch errors will be ignored; this is fine because we really have no
            // way of telling which error would occur first in the left-to-right order without keeping track of a lot more error
            // information -- so we play it safe, and register the guaranteed error

            // append the parameterless switches with left-to-right precedence, flagging duplicate switches as necessary
            for (int i = 0; i < (int)ParameterlessSwitch.NumberOfParameterlessSwitches; i++)
            {
                if (switchesToAppend.IsParameterlessSwitchSet((ParameterlessSwitch)i))
                {
                    if (!IsParameterlessSwitchSet((ParameterlessSwitch)i) ||
                        (s_parameterlessSwitchesMap[i].duplicateSwitchErrorMessage == null))
                    {
                        _parameterlessSwitches[i].commandLineArg = switchesToAppend._parameterlessSwitches[i].commandLineArg;
                    }
                    else
                    {
                        SetSwitchError(s_parameterlessSwitchesMap[i].duplicateSwitchErrorMessage,
                            switchesToAppend.GetParameterlessSwitchCommandLineArg((ParameterlessSwitch)i), commandLine);
                    }
                }
            }

            // append the parameterized switches with left-to-right precedence, flagging duplicate switches as necessary
            for (int j = 0; j < (int)ParameterizedSwitch.NumberOfParameterizedSwitches; j++)
            {
                if (switchesToAppend.IsParameterizedSwitchSet((ParameterizedSwitch)j))
                {
                    if (!IsParameterizedSwitchSet((ParameterizedSwitch)j) ||
                        (s_parameterizedSwitchesMap[j].duplicateSwitchErrorMessage == null))
                    {
                        if (_parameterizedSwitches[j].commandLineArg == null)
                        {
                            _parameterizedSwitches[j].parameters = new ArrayList();
                        }

                        _parameterizedSwitches[j].commandLineArg = switchesToAppend._parameterizedSwitches[j].commandLineArg;
                        _parameterizedSwitches[j].parameters.AddRange(switchesToAppend._parameterizedSwitches[j].parameters);
                    }
                    else
                    {
                        SetSwitchError(s_parameterizedSwitchesMap[j].duplicateSwitchErrorMessage,
                            switchesToAppend.GetParameterizedSwitchCommandLineArg((ParameterizedSwitch)j), commandLine);
                    }
                }
            }
        }
    }
}
