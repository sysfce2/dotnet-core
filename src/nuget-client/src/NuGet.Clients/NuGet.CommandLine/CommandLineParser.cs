// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NuGet.Commands;
using NuGet.Packaging;

namespace NuGet.CommandLine
{
    public class CommandLineParser
    {
        private readonly ICommandManager _commandManager;

        // On Unix or MacOSX slash as a switch indicator would interfere with the path separator
        private static readonly bool _supportSlashAsSwitch = (Environment.OSVersion.Platform != PlatformID.Unix) && (Environment.OSVersion.Platform != PlatformID.MacOSX);

        public CommandLineParser(ICommandManager manager)
        {
            _commandManager = manager;
        }

        public void ExtractOptions(ICommand command, IEnumerator<string> argsEnumerator)
        {
            List<string> arguments = new List<string>();
            IDictionary<OptionAttribute, PropertyInfo> properties = _commandManager.GetCommandOptions(command);

            while (true)
            {
                string option = GetNextCommandLineItem(argsEnumerator);

                if (option == null)
                {
                    break;
                }

                if (!(option.StartsWith("/", StringComparison.OrdinalIgnoreCase) && _supportSlashAsSwitch)
                    && !option.StartsWith("-", StringComparison.OrdinalIgnoreCase))
                {
                    arguments.Add(option);
                    continue;
                }

                string optionText = option.Substring(1);
                string value = null;

                if (optionText.EndsWith("-", StringComparison.OrdinalIgnoreCase))
                {
                    optionText = optionText.TrimEnd('-');
                    value = "false";
                }

                var result = GetPartialOptionMatch(properties, prop => prop.Value.Name, prop => prop.Key.AltName, option, optionText);
                PropertyInfo propInfo = result.Value;

                if (propInfo.PropertyType == typeof(bool))
                {
                    value = value ?? "true";
                }
                else
                {
                    value = GetNextCommandLineItem(argsEnumerator);
                }

                if (value == null)
                {
                    throw new CommandException(LocalizedResourceManager.GetString("MissingOptionValueError"), option);
                }

                AssignValue(command, propInfo, option, value);
            }
            command.Arguments.AddRange(arguments);
        }

        internal static void AssignValue(object command, PropertyInfo property, string option, object value)
        {
            try
            {
                if (TypeHelper.IsMultiValuedProperty(property))
                {
                    // If we were able to look up a parent of type ICollection<>, perform a Add operation on it.
                    // Note that we expect the value is a string.
                    var stringValue = (string)value;

                    dynamic list = property.GetValue(command, null);
                    // The parameter value is one or more semi-colon separated items that might support values also
                    // Example of a list value : nuget pack -option "foo;bar;baz"
                    // Example of a keyvalue value: nuget pack -option "foo=bar;baz=false"
                    foreach (var item in stringValue.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (TypeHelper.IsKeyValueProperty(property))
                        {
                            int eqIndex = item.IndexOf("=", StringComparison.OrdinalIgnoreCase);
                            if (eqIndex > -1)
                            {
                                string propertyKey = item.Substring(0, eqIndex);
                                string propertyValue = item.Substring(eqIndex + 1);
                                list.Add(propertyKey, propertyValue);
                            }
                        }
                        else
                        {
                            list.Add(item);
                        }
                    }
                }
                else if (TypeHelper.IsEnumProperty(property))
                {
                    var enumValue = Enum.GetValues(property.PropertyType).Cast<object>();
                    value = GetPartialOptionMatch(enumValue, e => e.ToString(), e => e.ToString(), option, value.ToString());
                    property.SetValue(command, value, index: null);
                }
                else
                {
                    property.SetValue(command, TypeHelper.ChangeType(value, property.PropertyType), index: null);
                }
            }
            catch (CommandException)
            {
                throw;
            }
            catch
            {
                throw new CommandException(LocalizedResourceManager.GetString("InvalidOptionValueError"), option, value);
            }
        }

        public ICommand ParseCommandLine(IEnumerable<string> commandLineArgs)
        {
            IEnumerator<string> argsEnumerator = commandLineArgs.GetEnumerator();

            // Get the desired command name
            string cmdName = GetNextCommandLineItem(argsEnumerator);
            if (cmdName == null)
            {
                return null;
            }

            // Get the command based on the name
            ICommand cmd = _commandManager.GetCommand(cmdName);
            if (cmd == null)
            {
                throw new CommandException(LocalizedResourceManager.GetString("UnknowCommandError"), cmdName);
            }

            ExtractOptions(cmd, argsEnumerator);
            return cmd;
        }

        public static string GetNextCommandLineItem(IEnumerator<string> argsEnumerator)
        {
            if (argsEnumerator == null || !argsEnumerator.MoveNext())
            {
                return null;
            }
            return argsEnumerator.Current;
        }

        private static TVal GetPartialOptionMatch<TVal>(IEnumerable<TVal> source, Func<TVal, string> getDisplayName, Func<TVal, string> getAltName, string option, string value)
        {
            var results = from item in source
                          where getDisplayName(item).StartsWith(value, StringComparison.OrdinalIgnoreCase) ||
                                (getAltName(item) ?? String.Empty).StartsWith(value, StringComparison.OrdinalIgnoreCase)
                          select item;
            var result = results.FirstOrDefault();
            if (!results.Any())
            {
                throw new CommandException(LocalizedResourceManager.GetString("UnknownOptionError"), option);
            }
            else if (results.Skip(1).Any())
            {
                try
                {
                    // When multiple results are found, if there's an exact match, return it.
                    result = results.First(c => value.Equals(getDisplayName(c), StringComparison.OrdinalIgnoreCase) ||
                                                value.Equals(getAltName(c), StringComparison.OrdinalIgnoreCase));
                }
                catch (InvalidOperationException)
                {
                    throw new CommandException(String.Format(CultureInfo.CurrentCulture, LocalizedResourceManager.GetString("AmbiguousOption"), value,
                        String.Join(" ", from c in results select getDisplayName(c))));
                }
            }
            return result;
        }
    }
}
