﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using Microsoft.CodeAnalysis.Collections;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Collections;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Options;

[Export(typeof(IGlobalOptionService)), Shared]
[method: ImportingConstructor]
[method: Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
internal sealed class GlobalOptionService(
    [ImportMany] IEnumerable<Lazy<IOptionPersisterProvider>> optionPersisterProviders) : IGlobalOptionService
{
    private readonly object _gate = new();

    #region Guarded by _gate

    private readonly Lazy<ImmutableArray<IOptionPersister>> _optionPersisters = new(() => GetOptionPersisters(optionPersisterProviders));
    private ImmutableDictionary<OptionKey2, object?> _currentValues = ImmutableDictionary.Create<OptionKey2, object?>();

    #endregion

    private readonly WeakEvent<OptionChangedEventArgs> _optionChanged = new();

    private static ImmutableArray<IOptionPersister> GetOptionPersisters(IEnumerable<Lazy<IOptionPersisterProvider>> optionPersisterProviders)
    {
        return optionPersisterProviders.SelectAsArray(
            static provider => provider.Value.GetOrCreatePersister());
    }

    private static object? LoadOptionFromPersisterOrGetDefault(OptionKey2 optionKey, ImmutableArray<IOptionPersister> persisters)
    {
        foreach (var persister in persisters)
        {
            if (persister.TryFetch(optionKey, out var persistedValue))
            {
                return persistedValue;
            }
        }

        // Just use the default. We will still cache this so we aren't trying to deserialize
        // over and over.
        return optionKey.Option.DefaultValue;
    }

    bool IOptionsReader.TryGetOption<T>(OptionKey2 optionKey, out T value)
    {
        value = GetOption<T>(optionKey);
        return true;
    }

    public T GetOption<T>(Option2<T> option)
        => GetOption<T>(new OptionKey2(option));

    public T GetOption<T>(PerLanguageOption2<T> option, string language)
        => GetOption<T>(new OptionKey2(option, language));

    public T GetOption<T>(OptionKey2 optionKey)
    {
        // Performance: This is called very frequently, with the vast majority (> 99%) of calls requesting a previously
        //  added key. In those cases, we can avoid taking the lock as _currentValues is an immutable structure.
        if (_currentValues.TryGetValue(optionKey, out var value))
        {
            return (T)value!;
        }

        // Ensure the option persisters are available before taking the global lock
        var persisters = _optionPersisters.Value;
        lock (_gate)
        {
            return (T)GetOption_NoLock(ref _currentValues, optionKey, persisters)!;
        }
    }

    public ImmutableArray<object?> GetOptions(ImmutableArray<OptionKey2> optionKeys)
    {
        using var values = TemporaryArray<object?>.Empty;

        // Performance: The vast majority of calls are for previously added keys. In those cases, we can avoid taking the lock
        //  as _currentValues is an immutable structure.
        var currentValues = _currentValues;
        foreach (var optionKey in optionKeys)
        {
            if (!currentValues.TryGetValue(optionKey, out var value))
            {
                values.Clear();
                break;
            }

            values.Add(value);
        }

        if (values.Count != optionKeys.Length)
        {
            // Ensure the option persisters are available before taking the global lock
            var persisters = _optionPersisters.Value;
            lock (_gate)
            {
                foreach (var optionKey in optionKeys)
                {
                    values.Add(GetOption_NoLock(ref _currentValues, optionKey, persisters));
                }
            }
        }

        return values.ToImmutableAndClear();
    }

    private static object? GetOption_NoLock(ref ImmutableDictionary<OptionKey2, object?> currentValues, OptionKey2 optionKey, ImmutableArray<IOptionPersister> persisters)
    {
        // The option must be internally defined and it can't be a legacy option whose value is mapped to another option:
        Debug.Assert(optionKey.Option is IOption2 { Definition.StorageMapping: null });

        if (currentValues.TryGetValue(optionKey, out var value))
        {
            return value;
        }

        value = LoadOptionFromPersisterOrGetDefault(optionKey, persisters);

        currentValues = currentValues.Add(optionKey, value);

        return value;
    }

    public void SetGlobalOption<T>(Option2<T> option, T value)
        => SetGlobalOption(new OptionKey2(option), value);

    public void SetGlobalOption<T>(PerLanguageOption2<T> option, string language, T value)
        => SetGlobalOption(new OptionKey2(option, language), value);

    public void SetGlobalOption(OptionKey2 optionKey, object? value)
        => SetGlobalOptions(OneOrMany.Create(KeyValuePair.Create(optionKey, value)));

    public bool SetGlobalOptions(ImmutableArray<KeyValuePair<OptionKey2, object?>> options)
        => SetGlobalOptions(OneOrMany.Create(options));

    private bool SetGlobalOptions(OneOrMany<KeyValuePair<OptionKey2, object?>> options)
    {
        using var _ = ArrayBuilder<(OptionKey2, object?)>.GetInstance(options.Count, out var changedOptions);
        var persisters = _optionPersisters.Value;

        lock (_gate)
        {
            var currentValues = _currentValues;
            foreach (var (optionKey, value) in options)
            {
                var existingValue = GetOption_NoLock(ref currentValues, optionKey, persisters);
                if (Equals(value, existingValue))
                {
                    continue;
                }

                currentValues = currentValues.SetItem(optionKey, value);
                changedOptions.Add((optionKey, value));
            }

            _currentValues = currentValues;
        }

        if (changedOptions.IsEmpty)
        {
            return false;
        }

        foreach (var (key, value) in changedOptions)
        {
            PersistOption(persisters, key, value);
        }

        RaiseOptionChangedEvent(changedOptions.ToImmutable());
        return true;
    }

    private static void PersistOption(ImmutableArray<IOptionPersister> persisters, OptionKey2 optionKey, object? value)
    {
        foreach (var persister in persisters)
        {
            if (persister.TryPersist(optionKey, value))
            {
                break;
            }
        }
    }

    public bool RefreshOption(OptionKey2 optionKey, object? newValue)
    {
        lock (_gate)
        {
            if (_currentValues.TryGetValue(optionKey, out var oldValue))
            {
                if (Equals(oldValue, newValue))
                {
                    // Value is still the same, no reason to raise events
                    return false;
                }
            }

            _currentValues = _currentValues.SetItem(optionKey, newValue);
        }

        RaiseOptionChangedEvent([(optionKey, newValue)]);
        return true;
    }

    public void AddOptionChangedHandler(object target, WeakEventHandler<OptionChangedEventArgs> handler)
    {
        _optionChanged.AddHandler(target, handler);
    }

    public void RemoveOptionChangedHandler(object target, WeakEventHandler<OptionChangedEventArgs> handler)
    {
        _optionChanged.RemoveHandler(target, handler);
    }

    private void RaiseOptionChangedEvent(ImmutableArray<(OptionKey2, object?)> changedOptions)
    {
        Debug.Assert(!changedOptions.IsEmpty);
        _optionChanged.RaiseEvent(this, new OptionChangedEventArgs(changedOptions));
    }

    internal TestAccessor GetTestAccessor()
    {
        return new TestAccessor(this);
    }

    internal readonly struct TestAccessor
    {
        private readonly GlobalOptionService _instance;

        internal TestAccessor(GlobalOptionService instance)
        {
            _instance = instance;
        }

        public void ClearCachedValues()
        {
            lock (_instance._gate)
            {
                _instance._currentValues = ImmutableDictionary.Create<OptionKey2, object?>();
            }
        }
    }
}
