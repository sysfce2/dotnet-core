﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Drawing;
using System.Formats.Nrbf;
using System.Private.Windows.BinaryFormat;
using System.Private.Windows.Nrbf;
using System.Runtime.Serialization.Formatters.Binary;
using FormatTests.Common;

namespace FormatTests.FormattedObject;

public class ListTests : SerializationTest
{
    public static TheoryData<ArrayList> ArrayLists_TestData => new()
    {
        new ArrayList(),
        new ArrayList()
        {
            int.MaxValue,
            uint.MaxValue,
            long.MaxValue,
            ulong.MaxValue,
            short.MaxValue,
            ushort.MaxValue,
            byte.MaxValue,
            sbyte.MaxValue,
            true,
            float.MaxValue,
            double.MaxValue,
            char.MaxValue,
            TimeSpan.MaxValue,
            DateTime.MaxValue,
            decimal.MaxValue,
            "You betcha"
        },
        new ArrayList() { "Same", "old", "same", "old" }
    };

    public static TheoryData<ArrayList> ArrayLists_UnsupportedTestData => new()
    {
        new ArrayList()
        {
            new object(),
        },
        new ArrayList()
        {
            int.MaxValue,
            default(Point)
        }
    };

    [Theory]
    [MemberData(nameof(PrimitiveLists_TestData))]
    public void BinaryFormatWriter_TryWritePrimitiveList(IList list)
    {
        using MemoryStream stream = new();
        BinaryFormatWriter.TryWritePrimitiveList(stream, list).Should().BeTrue();
        stream.Position = 0;

        // cs/binary-formatter-without-binder
        BinaryFormatter formatter = new(); // CodeQL [SM04191] : This is a test. Safe use because the deserialization process is performed on trusted data and the types are controlled and validated.
        // cs/dangerous-binary-deserialization
        IList deserialized = (IList)formatter.Deserialize(stream); // CodeQL[SM02229] : Testing legacy feature. This is a safe use of BinaryFormatter because the data is trusted and the types are controlled and validated.

        deserialized.Should().BeEquivalentTo(list);
    }

    [Theory]
    [MemberData(nameof(Lists_UnsupportedTestData))]
    public void BinaryFormatWriter_TryWritePrimitiveList_Unsupported(IList list)
    {
        using MemoryStream stream = new();
        BinaryFormatWriter.TryWritePrimitiveList(stream, list).Should().BeFalse();
        stream.Position.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(PrimitiveLists_TestData))]
    public void SerializationRecordExtensions_TryGetPrimitiveList(IList list)
    {
        SerializationRecord rootRecord = list.SerializeAndDecode();
        rootRecord.TryGetPrimitiveList(out object? deserialized).Should().BeTrue();
        deserialized.Should().BeEquivalentTo(list);
    }

    public static TheoryData<IList> PrimitiveLists_TestData => new()
    {
        new List<int>(),
        new List<bool>() { true, false},
        new List<float>() { 3.14f },
        new List<float>() { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.NegativeZero },
        new List<int>() { 1, 3, -4, 5, 6, 7 },
        new List<uint>() { 0, 2, uint.MaxValue, uint.MinValue },
        new List<sbyte>() { 0, -2, sbyte.MaxValue, sbyte.MinValue },
        new List<byte>() { 0xDE, 0xAD, 0xBE, 0xEF },
        new List<short>() { 0, -2, short.MinValue, short.MaxValue },
        new List<ushort>() { 1, 2, ushort.MinValue, ushort.MaxValue },
        new List<long>() { 0, -2, long.MinValue, long.MaxValue },
        new List<ulong>() { 1, 2, ulong.MinValue, ulong.MaxValue },
        new List<double>() { 3.14, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.NegativeZero },
        new List<char>() { 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h' },
        new List<char>() { 'a', '\0', 'c' },
        new List<string>() { "Believe", "it", "or", "not" },
        new List<decimal>() { 42m },
        new List<DateTime>() { new(2000, 1, 1) },
        new List<TimeSpan>() { new(0, 0, 50) }
    };

    public static TheoryData<IList> Lists_UnsupportedTestData => new()
    {
        new List<object>(),
        new List<nint>(),
        new List<(int, int)>()
    };
}
