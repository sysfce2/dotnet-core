// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;

// ReSharper disable ParameterOnlyUsedForPreconditionCheck.Local
// ReSharper disable InconsistentNaming
namespace Microsoft.EntityFrameworkCore;

public abstract class PropertyValuesTestBase<TFixture>(TFixture fixture) : IClassFixture<TFixture>
    where TFixture : PropertyValuesTestBase<TFixture>.PropertyValuesFixtureBase, new()
{
    protected TFixture Fixture { get; } = fixture;

    [ConditionalFact]
    public virtual Task Scalar_current_values_can_be_accessed_as_a_property_dictionary()
        => TestPropertyValuesScalars(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Scalar_original_values_can_be_accessed_as_a_property_dictionary()
        => TestPropertyValuesScalars(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Scalar_store_values_can_be_accessed_as_a_property_dictionary()
        => TestPropertyValuesScalars(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Scalar_store_values_can_be_accessed_asynchronously_as_a_property_dictionary()
        => TestPropertyValuesScalars(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestPropertyValuesScalars(
        Func<EntityEntry<Building>, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        building.Name = "Building One Prime";
        building.Value = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "Pine Walk";

        var values = await getPropertyValues(context.Entry(building));

        if (expectOriginalValues)
        {
            Assert.Equal("Building One", values["Name"]);
            Assert.Equal(1500000m, values["Value"]);
            Assert.Equal(11, values["Shadow1"]);
            Assert.Equal("Meadow Drive", values["Shadow2"]);
        }
        else
        {
            Assert.Equal("Building One Prime", values["Name"]);
            Assert.Equal(1500001m, values["Value"]);
            Assert.Equal(12, values["Shadow1"]);
            Assert.Equal("Pine Walk", values["Shadow2"]);
        }

        Assert.True(building.CreatedCalled);
        Assert.True(building.InitializingCalled);
        Assert.True(building.InitializedCalled);
    }

    [ConditionalFact]
    public virtual Task Scalar_current_values_can_be_accessed_as_a_property_dictionary_using_IProperty()
        => TestPropertyValuesScalarsIProperty(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Scalar_original_values_can_be_accessed_as_a_property_dictionary_using_IProperty()
        => TestPropertyValuesScalarsIProperty(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Scalar_store_values_can_be_accessed_as_a_property_dictionary_using_IProperty()
        => TestPropertyValuesScalarsIProperty(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Scalar_store_values_can_be_accessed_asynchronously_as_a_property_dictionary_using_IProperty()
        => TestPropertyValuesScalarsIProperty(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestPropertyValuesScalarsIProperty(
        Func<EntityEntry<Building>, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        building.Name = "Building One Prime";
        building.Value = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "Pine Walk";

        var entry = context.Entry(building);
        var values = await getPropertyValues(entry);

        if (expectOriginalValues)
        {
            Assert.Equal("Building One", values[entry.Property(e => e.Name).Metadata]);
            Assert.Equal(1500000m, values[entry.Property(e => e.Value).Metadata]);
            Assert.Equal(11, values[entry.Property("Shadow1").Metadata]);
            Assert.Equal("Meadow Drive", values[entry.Property("Shadow2").Metadata]);
        }
        else
        {
            Assert.Equal("Building One Prime", values[entry.Property(e => e.Name).Metadata]);
            Assert.Equal(1500001m, values[entry.Property(e => e.Value).Metadata]);
            Assert.Equal(12, values[entry.Property("Shadow1").Metadata]);
            Assert.Equal("Pine Walk", values[entry.Property("Shadow2").Metadata]);
        }

        Assert.True(building.CreatedCalled);
        Assert.True(building.InitializingCalled);
        Assert.True(building.InitializedCalled);
    }

    [ConditionalFact]
    public virtual Task Scalar_current_values_of_a_derived_object_can_be_accessed_as_a_property_dictionary()
        => TestPropertyValuesDerivedScalars(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Scalar_original_values_of_a_derived_object_can_be_accessed_as_a_property_dictionary()
        => TestPropertyValuesDerivedScalars(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Scalar_store_values_of_a_derived_object_can_be_accessed_as_a_property_dictionary()
        => TestPropertyValuesDerivedScalars(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Scalar_store_values_of_a_derived_object_can_be_accessed_asynchronously_as_a_property_dictionary()
        => TestPropertyValuesDerivedScalars(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestPropertyValuesDerivedScalars(
        Func<EntityEntry<CurrentEmployee>, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        var employee = context.Set<Employee>().OfType<CurrentEmployee>().Single(b => b.FirstName == "Rowan");

        employee.LastName = "Milner";
        employee.LeaveBalance = 55m;
        context.Entry(employee).Property("Shadow1").CurrentValue = 222;
        context.Entry(employee).Property("Shadow2").CurrentValue = "Dev";
        context.Entry(employee).Property("Shadow3").CurrentValue = 2222;

        var values = await getPropertyValues(context.Entry(employee));

        if (expectOriginalValues)
        {
            Assert.Equal("Miller", values["LastName"]);
            Assert.Equal(45m, values["LeaveBalance"]);
            Assert.Equal(111, values["Shadow1"]);
            Assert.Equal("PM", values["Shadow2"]);
            Assert.Equal(1111, values["Shadow3"]);
        }
        else
        {
            Assert.Equal("Milner", values["LastName"]);
            Assert.Equal(55m, values["LeaveBalance"]);
            Assert.Equal(222, values["Shadow1"]);
            Assert.Equal("Dev", values["Shadow2"]);
            Assert.Equal(2222, values["Shadow3"]);
        }

        Assert.True(employee.CreatedCalled);
        Assert.True(employee.InitializingCalled);
        Assert.True(employee.InitializedCalled);
    }

    [ConditionalFact]
    public virtual Task Scalar_current_values_can_be_accessed_as_a_non_generic_property_dictionary()
        => TestNonGenericPropertyValuesScalars(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Scalar_original_values_can_be_accessed_as_a_non_generic_property_dictionary()
        => TestNonGenericPropertyValuesScalars(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Scalar_store_values_can_be_accessed_as_a_non_generic_property_dictionary()
        => TestNonGenericPropertyValuesScalars(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Scalar_store_values_can_be_accessed_asynchronously_as_a_non_generic_property_dictionary()
        => TestNonGenericPropertyValuesScalars(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestNonGenericPropertyValuesScalars(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        context.Entry(building).Property("Name").CurrentValue = "Building One Prime";
        context.Entry(building).Property("Value").CurrentValue = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "Pine Walk";

        var values = await getPropertyValues(context.Entry(building));

        if (expectOriginalValues)
        {
            Assert.Equal("Building One", values["Name"]);
            Assert.Equal(1500000m, values["Value"]);
            Assert.Equal(11, values["Shadow1"]);
            Assert.Equal("Meadow Drive", values["Shadow2"]);

            Assert.Equal("Building One", values.GetValue<string>("Name"));
            Assert.Equal(1500000m, values.GetValue<decimal>("Value"));
            Assert.Equal(11, values.GetValue<int>("Shadow1"));
            Assert.Equal("Meadow Drive", values.GetValue<string>("Shadow2"));
        }
        else
        {
            Assert.Equal("Building One Prime", values["Name"]);
            Assert.Equal(1500001m, values["Value"]);
            Assert.Equal(12, values["Shadow1"]);
            Assert.Equal("Pine Walk", values["Shadow2"]);

            Assert.Equal("Building One Prime", values.GetValue<string>("Name"));
            Assert.Equal(1500001m, values.GetValue<decimal>("Value"));
            Assert.Equal(12, values.GetValue<int>("Shadow1"));
            Assert.Equal("Pine Walk", values.GetValue<string>("Shadow2"));
        }

        Assert.True(building.CreatedCalled);
        Assert.True(building.InitializingCalled);
        Assert.True(building.InitializedCalled);
    }

    [ConditionalFact]
    public virtual Task Scalar_current_values_can_be_accessed_as_a_non_generic_property_dictionary_using_IProperty()
        => TestNonGenericPropertyValuesScalarsIProperty(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Scalar_original_values_can_be_accessed_as_a_non_generic_property_dictionary_using_IProperty()
        => TestNonGenericPropertyValuesScalarsIProperty(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Scalar_store_values_can_be_accessed_as_a_non_generic_property_dictionary_using_IProperty()
        => TestNonGenericPropertyValuesScalarsIProperty(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Scalar_store_values_can_be_accessed_asynchronously_as_a_non_generic_property_dictionary_using_IProperty()
        => TestNonGenericPropertyValuesScalarsIProperty(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestNonGenericPropertyValuesScalarsIProperty(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        object building = context.Set<Building>().Single(b => b.Name == "Building One");

        context.Entry(building).Property("Name").CurrentValue = "Building One Prime";
        context.Entry(building).Property("Value").CurrentValue = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "Pine Walk";

        var entry = context.Entry(building);
        var values = await getPropertyValues(entry);

        if (expectOriginalValues)
        {
            Assert.Equal("Building One", values["Name"]);
            Assert.Equal(1500000m, values["Value"]);
            Assert.Equal(11, values["Shadow1"]);
            Assert.Equal("Meadow Drive", values["Shadow2"]);

            Assert.Equal("Building One", values.GetValue<string>(entry.Property("Name").Metadata));
            Assert.Equal(1500000m, values.GetValue<decimal>(entry.Property("Value").Metadata));
            Assert.Equal(11, values.GetValue<int>(entry.Property("Shadow1").Metadata));
            Assert.Equal("Meadow Drive", values.GetValue<string>(entry.Property("Shadow2").Metadata));
        }
        else
        {
            Assert.Equal("Building One Prime", values["Name"]);
            Assert.Equal(1500001m, values["Value"]);
            Assert.Equal(12, values["Shadow1"]);
            Assert.Equal("Pine Walk", values["Shadow2"]);

            Assert.Equal("Building One Prime", values.GetValue<string>(entry.Property("Name").Metadata));
            Assert.Equal(1500001m, values.GetValue<decimal>(entry.Property("Value").Metadata));
            Assert.Equal(12, values.GetValue<int>(entry.Property("Shadow1").Metadata));
            Assert.Equal("Pine Walk", values.GetValue<string>(entry.Property("Shadow2").Metadata));
        }
    }

    [ConditionalFact]
    public virtual Task Scalar_current_values_of_a_derived_object_can_be_accessed_as_a_non_generic_property_dictionary()
        => TestNonGenericPropertyValuesDerivedScalars(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Scalar_original_values_of_a_derived_object_can_be_accessed_as_a_non_generic_property_dictionary()
        => TestNonGenericPropertyValuesDerivedScalars(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Scalar_store_values_of_a_derived_object_can_be_accessed_as_a_non_generic_property_dictionary()
        => TestNonGenericPropertyValuesDerivedScalars(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Scalar_store_values_of_a_derived_object_can_be_accessed_asynchronously_as_a_non_generic_property_dictionary()
        => TestNonGenericPropertyValuesDerivedScalars(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestNonGenericPropertyValuesDerivedScalars(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        object employee = context.Set<Employee>().OfType<CurrentEmployee>().Single(b => b.FirstName == "Rowan");

        ((CurrentEmployee)employee).LastName = "Milner";
        ((CurrentEmployee)employee).LeaveBalance = 55m;
        context.Entry(employee).Property("Shadow1").CurrentValue = 222;
        context.Entry(employee).Property("Shadow2").CurrentValue = "Dev";
        context.Entry(employee).Property("Shadow3").CurrentValue = 2222;

        var values = await getPropertyValues(context.Entry(employee));

        if (expectOriginalValues)
        {
            Assert.Equal("Miller", values["LastName"]);
            Assert.Equal(45m, values["LeaveBalance"]);
            Assert.Equal(111, values["Shadow1"]);
            Assert.Equal("PM", values["Shadow2"]);
            Assert.Equal(1111, values["Shadow3"]);
        }
        else
        {
            Assert.Equal("Milner", values["LastName"]);
            Assert.Equal(55m, values["LeaveBalance"]);
            Assert.Equal(222, values["Shadow1"]);
            Assert.Equal("Dev", values["Shadow2"]);
            Assert.Equal(2222, values["Shadow3"]);
        }
    }

    [ConditionalFact]
    public virtual void Scalar_current_values_can_be_set_using_a_property_dictionary()
        => TestSetPropertyValuesScalars(e => e.CurrentValues, (e, n) => e.Property(n).CurrentValue!);

    [ConditionalFact]
    public virtual void Scalar_original_values_can_be_set_using_a_property_dictionary()
        => TestSetPropertyValuesScalars(e => e.OriginalValues, (e, n) => e.Property(n).OriginalValue!);

    private void TestSetPropertyValuesScalars(
        Func<EntityEntry<Building>, PropertyValues> getPropertyValues,
        Func<EntityEntry, string, object> getValue)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var values = getPropertyValues(context.Entry(building));

        values["Name"] = "Building 18";
        values["Value"] = -1000m;
        values["Shadow1"] = 13;
        values["Shadow2"] = "Pine Walk";

        Assert.Equal("Building 18", values["Name"]);
        Assert.Equal(-1000m, values["Value"]);
        Assert.Equal(13, values["Shadow1"]);
        Assert.Equal("Pine Walk", values["Shadow2"]);

        var entry = context.Entry(building);
        Assert.Equal("Building 18", getValue(entry, "Name"));
        Assert.Equal(-1000m, getValue(entry, "Value"));
        Assert.Equal(13, getValue(entry, "Shadow1"));
        Assert.Equal("Pine Walk", getValue(entry, "Shadow2"));
    }

    [ConditionalFact]
    public virtual void Scalar_current_values_can_be_set_using_a_property_dictionary_with_IProperty()
        => TestSetPropertyValuesScalarsIProperty(e => e.CurrentValues, (e, n) => e.Property(n).CurrentValue!);

    [ConditionalFact]
    public virtual void Scalar_original_values_can_be_set_using_a_property_dictionary_with_IProperty()
        => TestSetPropertyValuesScalarsIProperty(e => e.OriginalValues, (e, n) => e.Property(n).OriginalValue!);

    private void TestSetPropertyValuesScalarsIProperty(
        Func<EntityEntry<Building>, PropertyValues> getPropertyValues,
        Func<EntityEntry, string, object> getValue)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var entry = context.Entry(building);
        var values = getPropertyValues(entry);

        values[entry.Property(e => e.Name).Metadata] = "Building 18";
        values[entry.Property(e => e.Value).Metadata] = -1000m;
        values[entry.Property("Shadow1").Metadata] = 13;
        values[entry.Property("Shadow2").Metadata] = "Pine Walk";

        Assert.Equal("Building 18", values["Name"]);
        Assert.Equal(-1000m, values["Value"]);
        Assert.Equal(13, values["Shadow1"]);
        Assert.Equal("Pine Walk", values["Shadow2"]);

        Assert.Equal("Building 18", getValue(entry, "Name"));
        Assert.Equal(-1000m, getValue(entry, "Value"));
        Assert.Equal(13, getValue(entry, "Shadow1"));
        Assert.Equal("Pine Walk", getValue(entry, "Shadow2"));
    }

    [ConditionalFact]
    public virtual void Scalar_current_values_can_be_set_using_a_non_generic_property_dictionary()
        => TestSetNonGenericPropertyValuesScalars(e => e.CurrentValues, (e, n) => e.Property(n).CurrentValue!);

    [ConditionalFact]
    public virtual void Scalar_original_values_can_be_set_using_a_non_generic_property_dictionary()
        => TestSetNonGenericPropertyValuesScalars(e => e.OriginalValues, (e, n) => e.Property(n).OriginalValue!);

    private void TestSetNonGenericPropertyValuesScalars(
        Func<EntityEntry, PropertyValues> getPropertyValues,
        Func<EntityEntry, string, object> getValue)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var values = getPropertyValues(context.Entry(building));

        values["Name"] = "Building 18";
        values["Value"] = -1000m;
        values["Shadow1"] = 13;
        values["Shadow2"] = "Pine Walk";

        Assert.Equal("Building 18", values["Name"]);
        Assert.Equal(-1000m, values["Value"]);
        Assert.Equal(13, values["Shadow1"]);
        Assert.Equal("Pine Walk", values["Shadow2"]);

        var entry = context.Entry(building);
        Assert.Equal("Building 18", getValue(entry, "Name"));
        Assert.Equal(-1000m, getValue(entry, "Value"));
        Assert.Equal(13, getValue(entry, "Shadow1"));
        Assert.Equal("Pine Walk", getValue(entry, "Shadow2"));
    }

    [ConditionalFact]
    public virtual Task Complex_current_values_can_be_accessed_as_a_property_dictionary_using_IProperty()
        => TestPropertyValuesComplexIProperty(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Complex_original_values_can_be_accessed_as_a_property_dictionary_using_IProperty()
        => TestPropertyValuesComplexIProperty(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Complex_store_values_can_be_accessed_as_a_property_dictionary_using_IProperty()
        => TestPropertyValuesComplexIProperty(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Complex_store_values_can_be_accessed_asynchronously_as_a_property_dictionary_using_IProperty()
        => TestPropertyValuesComplexIProperty(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestPropertyValuesComplexIProperty(
        Func<EntityEntry<Building>, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var original = Building.Create(building.BuildingId, building.Name!, building.Value);
        var changed = Building.Create(building.BuildingId, building.Name!, building.Value, 1);

        building.Culture = changed.Culture;
        building.Milk.Rating = changed.Milk.Rating;
        building.Milk.License = changed.Milk.License;
        building.Milk.Manufacturer = changed.Milk.Manufacturer;

        var entry = context.Entry(building);
        var values = await getPropertyValues(entry);

        var cultureEntry = entry.ComplexProperty(e => e.Culture);
        var cultureManufacturerEntry = cultureEntry.ComplexProperty(e => e.Manufacturer);
        var cultureLicenseEntry = cultureEntry.ComplexProperty(e => e.License);
        var cultureManTogEntry = cultureManufacturerEntry.ComplexProperty(e => e.Tog);
        var cultureManTagEntry = cultureManufacturerEntry.ComplexProperty(e => e.Tag);
        var cultureLicTogEntry = cultureLicenseEntry.ComplexProperty(e => e.Tog);
        var cultureLicTagEntry = cultureLicenseEntry.ComplexProperty(e => e.Tag);

        var milkEntry = entry.ComplexProperty(e => e.Milk);
        var milkManufacturerEntry = milkEntry.ComplexProperty(e => e.Manufacturer);
        var milkLicenseEntry = milkEntry.ComplexProperty(e => e.License);
        var milkManTogEntry = milkManufacturerEntry.ComplexProperty(e => e.Tog);
        var milkManTagEntry = milkManufacturerEntry.ComplexProperty(e => e.Tag);
        var milkLicTogEntry = milkLicenseEntry.ComplexProperty(e => e.Tog);
        var milkLicTagEntry = milkLicenseEntry.ComplexProperty(e => e.Tag);

        var expected = expectOriginalValues ? original : changed;
        Assert.Equal(expected.Culture.Rating, values[cultureEntry.Property(e => e.Rating).Metadata]);
        Assert.Equal(expected.Culture.Species, values[cultureEntry.Property(e => e.Species).Metadata]);
        Assert.Equal(expected.Culture.Subspecies, values[cultureEntry.Property(e => e.Subspecies).Metadata]);
        Assert.Equal(expected.Culture.Validation, values[cultureEntry.Property(e => e.Validation).Metadata]);
        Assert.Equal(expected.Culture.Manufacturer.Name, values[cultureManufacturerEntry.Property(e => e.Name).Metadata]);
        Assert.Equal(expected.Culture.Manufacturer.Rating, values[cultureManufacturerEntry.Property(e => e.Rating).Metadata]);
        Assert.Equal(expected.Culture.Manufacturer.Tog.Text, values[cultureManTogEntry.Property(e => e.Text).Metadata]);
        Assert.Equal(expected.Culture.Manufacturer.Tag.Text, values[cultureManTagEntry.Property(e => e.Text).Metadata]);
        Assert.Equal(expected.Culture.License.Title, values[cultureLicenseEntry.Property(e => e.Title).Metadata]);
        Assert.Equal(expected.Culture.License.Charge, values[cultureLicenseEntry.Property(e => e.Charge).Metadata]);
        Assert.Equal(expected.Culture.License.Tog.Text, values[cultureLicTogEntry.Property(e => e.Text).Metadata]);
        Assert.Equal(expected.Culture.License.Tag.Text, values[cultureLicTagEntry.Property(e => e.Text).Metadata]);
        Assert.Equal(expected.Milk.Rating, values[milkEntry.Property(e => e.Rating).Metadata]);
        Assert.Equal(expected.Milk.Manufacturer.Name, values[milkManufacturerEntry.Property(e => e.Name).Metadata]);
        Assert.Equal(expected.Milk.Manufacturer.Rating, values[milkManufacturerEntry.Property(e => e.Rating).Metadata]);
        Assert.Equal(expected.Milk.Manufacturer.Tog.Text, values[milkManTogEntry.Property(e => e.Text).Metadata]);
        Assert.Equal(expected.Milk.Manufacturer.Tag.Text, values[milkManTagEntry.Property(e => e.Text).Metadata]);
        Assert.Equal(expected.Milk.License.Title, values[milkLicenseEntry.Property(e => e.Title).Metadata]);
        Assert.Equal(expected.Milk.License.Charge, values[milkLicenseEntry.Property(e => e.Charge).Metadata]);
        Assert.Equal(expected.Milk.License.Tog.Text, values[milkLicTogEntry.Property(e => e.Text).Metadata]);
        Assert.Equal(expected.Milk.License.Tag.Text, values[milkLicTagEntry.Property(e => e.Text).Metadata]);

        if (expectOriginalValues)
        {
            Assert.Equal(original.Milk.Species, values[milkEntry.Property(e => e.Species).Metadata]);
            Assert.Equal(original.Milk.Subspecies, values[milkEntry.Property(e => e.Subspecies).Metadata]);
            Assert.Equal(original.Milk.Validation, values[milkEntry.Property(e => e.Validation).Metadata]);
        }
        else
        {
            Assert.Equal(building.Milk.Species, values[milkEntry.Property(e => e.Species).Metadata]);
            Assert.Equal(building.Milk.Subspecies, values[milkEntry.Property(e => e.Subspecies).Metadata]);
            Assert.Equal(building.Milk.Validation, values[milkEntry.Property(e => e.Validation).Metadata]);
        }

        Assert.True(building.CreatedCalled);
        Assert.True(building.InitializingCalled);
        Assert.True(building.InitializedCalled);
    }

    [ConditionalFact]
    public virtual Task Current_values_can_be_copied_into_an_object()
        => TestPropertyValuesClone(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Original_values_can_be_copied_into_an_object()
        => TestPropertyValuesClone(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_copied_into_an_object()
        => TestPropertyValuesClone(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_copied_into_an_object_asynchronously()
        => TestPropertyValuesClone(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestPropertyValuesClone(
        Func<EntityEntry<Building>, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        building.Name = "Building One Prime";
        building.Value = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "Pine Walk";

        var buildingClone = (Building)(await getPropertyValues(context.Entry(building))).ToObject();

        if (expectOriginalValues)
        {
            Assert.Equal("Building One", buildingClone.Name);
            Assert.Equal(1500000m, buildingClone.Value);
        }
        else
        {
            Assert.Equal("Building One Prime", buildingClone.Name);
            Assert.Equal(1500001m, buildingClone.Value);
        }

        Assert.True(buildingClone.CreatedCalled);
        Assert.True(buildingClone.InitializingCalled);
        Assert.True(buildingClone.InitializedCalled);
    }

    [ConditionalFact]
    public virtual Task Current_values_for_derived_object_can_be_copied_into_an_object()
        => TestPropertyValuesDerivedClone(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Original_values_for_derived_object_can_be_copied_into_an_object()
        => TestPropertyValuesDerivedClone(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_for_derived_object_can_be_copied_into_an_object()
        => TestPropertyValuesDerivedClone(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_for_derived_object_can_be_copied_into_an_object_asynchronously()
        => TestPropertyValuesDerivedClone(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestPropertyValuesDerivedClone(
        Func<EntityEntry<CurrentEmployee>, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        var employee = context.Set<Employee>().OfType<CurrentEmployee>().Single(b => b.FirstName == "Rowan");

        employee.LastName = "Milner";
        employee.LeaveBalance = 55m;
        context.Entry(employee).Property("Shadow1").CurrentValue = 222;
        context.Entry(employee).Property("Shadow2").CurrentValue = "Dev";
        context.Entry(employee).Property("Shadow3").CurrentValue = 2222;

        var clone = (CurrentEmployee)(await getPropertyValues(context.Entry(employee))).ToObject();

        if (expectOriginalValues)
        {
            Assert.Equal("Rowan", clone.FirstName);
            Assert.Equal("Miller", clone.LastName);
            Assert.Equal(45m, clone.LeaveBalance);
        }
        else
        {
            Assert.Equal("Rowan", clone.FirstName);
            Assert.Equal("Milner", clone.LastName);
            Assert.Equal(55m, clone.LeaveBalance);
        }

        Assert.True(clone.CreatedCalled);
        Assert.True(clone.InitializingCalled);
        Assert.True(clone.InitializedCalled);
    }

    [ConditionalFact]
    public virtual Task Current_values_for_join_entity_can_be_copied_into_an_object()
        => TestPropertyValuesJoinEntityClone(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Original_values_for_join_entity_can_be_copied_into_an_object()
        => TestPropertyValuesJoinEntityClone(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_for_join_entity_can_be_copied_into_an_object()
        => TestPropertyValuesJoinEntityClone(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_for_join_entity_can_be_copied_into_an_object_asynchronously()
        => TestPropertyValuesJoinEntityClone(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestPropertyValuesJoinEntityClone(
        Func<EntityEntry<Dictionary<string, object>>, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();

        var employee = context.Set<Employee>()
            .OfType<CurrentEmployee>()
            .Include(e => e.VirtualTeams)
            .Single(b => b.FirstName == "Rowan");

        foreach (var joinEntry in context.ChangeTracker.Entries<Dictionary<string, object>>())
        {
            joinEntry.Property("Payload").CurrentValue = "Payload++";

            var clone = (Dictionary<string, object>)(await getPropertyValues(joinEntry)).ToObject();

            Assert.True((bool)clone["CreatedCalled"]);
            Assert.True((bool)clone["InitializingCalled"]);
            Assert.True((bool)clone["InitializedCalled"]);

            if (expectOriginalValues)
            {
                Assert.Equal("Payload", clone["Payload"]);
            }
            else
            {
                Assert.Equal("Payload++", clone["Payload"]);
            }
        }
    }

    [ConditionalFact]
    public virtual Task Current_values_can_be_copied_from_a_non_generic_property_dictionary_into_an_object()
        => TestNonGenericPropertyValuesClone(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Original_values_can_be_copied_non_generic_property_dictionary_into_an_object()
        => TestNonGenericPropertyValuesClone(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_copied_non_generic_property_dictionary_into_an_object()
        => TestNonGenericPropertyValuesClone(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_copied_asynchronously_non_generic_property_dictionary_into_an_object()
        => TestNonGenericPropertyValuesClone(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestNonGenericPropertyValuesClone(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        object building = context.Set<Building>().Single(b => b.Name == "Building One");

        ((Building)building).Name = "Building One Prime";
        ((Building)building).Value = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "Pine Walk";

        var buildingClone = (Building)(await getPropertyValues(context.Entry(building))).ToObject();

        if (expectOriginalValues)
        {
            Assert.Equal("Building One", buildingClone.Name);
            Assert.Equal(1500000m, buildingClone.Value);
        }
        else
        {
            Assert.Equal("Building One Prime", buildingClone.Name);
            Assert.Equal(1500001m, buildingClone.Value);
        }

        Assert.True(buildingClone.CreatedCalled);
        Assert.True(buildingClone.InitializingCalled);
        Assert.True(buildingClone.InitializedCalled);
    }

    [ConditionalFact]
    public virtual Task Current_values_can_be_copied_into_a_cloned_dictionary()
        => TestPropertyValuesCloneToValues(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Original_values_can_be_copied_into_a_cloned_dictionary()
        => TestPropertyValuesCloneToValues(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_copied_into_a_cloned_dictionary()
        => TestPropertyValuesCloneToValues(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_copied_into_a_cloned_dictionary_asynchronously()
        => TestPropertyValuesCloneToValues(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestPropertyValuesCloneToValues(
        Func<EntityEntry<Building>, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        building.Name = "Building One Prime";
        building.Value = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "The Avenue";

        var buildingValues = await getPropertyValues(context.Entry(building));
        var clonedBuildingValues = buildingValues.Clone();

        if (expectOriginalValues)
        {
            Assert.Equal("Building One", clonedBuildingValues["Name"]);
            Assert.Equal(1500000m, clonedBuildingValues["Value"]);
            Assert.Equal(11, clonedBuildingValues["Shadow1"]);
            Assert.Equal("Meadow Drive", clonedBuildingValues["Shadow2"]);
        }
        else
        {
            Assert.Equal("Building One Prime", clonedBuildingValues["Name"]);
            Assert.Equal(1500001m, clonedBuildingValues["Value"]);
            Assert.Equal(12, clonedBuildingValues["Shadow1"]);
            Assert.Equal("The Avenue", clonedBuildingValues["Shadow2"]);
        }

        // Test modification of cloned property values does not impact original property values

        var newKey = new Guid();
        clonedBuildingValues["BuildingId"] = newKey; // Can change primary key on clone
        clonedBuildingValues["Name"] = "Building 18";
        clonedBuildingValues["Shadow1"] = 13;
        clonedBuildingValues["Shadow2"] = "Pine Walk";

        if (expectOriginalValues)
        {
            Assert.Equal(newKey, clonedBuildingValues["BuildingId"]);
            Assert.Equal("Building 18", clonedBuildingValues["Name"]);
            Assert.Equal(13, clonedBuildingValues["Shadow1"]);
            Assert.Equal("Pine Walk", clonedBuildingValues["Shadow2"]);

            Assert.Equal("Building One", buildingValues["Name"]);
            Assert.Equal(11, buildingValues["Shadow1"]);
            Assert.Equal("Meadow Drive", buildingValues["Shadow2"]);
        }
        else
        {
            Assert.Equal(newKey, clonedBuildingValues["BuildingId"]);
            Assert.Equal("Building 18", clonedBuildingValues["Name"]);
            Assert.Equal(13, clonedBuildingValues["Shadow1"]);
            Assert.Equal("Pine Walk", clonedBuildingValues["Shadow2"]);

            Assert.Equal("Building One Prime", buildingValues["Name"]);
            Assert.Equal(12, buildingValues["Shadow1"]);
            Assert.Equal("The Avenue", buildingValues["Shadow2"]);
        }
    }

    [ConditionalFact]
    public virtual void Values_in_cloned_dictionary_can_be_set_with_IProperty()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var entry = context.Entry(building);

        var buildingValues = entry.CurrentValues;
        var clonedBuildingValues = buildingValues.Clone();

        Assert.Equal("Building One", clonedBuildingValues["Name"]);
        Assert.Equal(1500000m, clonedBuildingValues["Value"]);
        Assert.Equal(11, clonedBuildingValues["Shadow1"]);
        Assert.Equal("Meadow Drive", clonedBuildingValues["Shadow2"]);

        // Test modification of cloned property values does not impact original property values

        var newKey = new Guid();
        clonedBuildingValues[entry.Property(e => e.BuildingId).Metadata] = newKey; // Can change primary key on clone
        clonedBuildingValues[entry.Property(e => e.Name).Metadata] = "Building 18";
        clonedBuildingValues[entry.Property("Shadow1").Metadata] = 13;
        clonedBuildingValues[entry.Property("Shadow2").Metadata] = "Pine Walk";

        Assert.Equal(newKey, clonedBuildingValues["BuildingId"]);
        Assert.Equal("Building 18", clonedBuildingValues["Name"]);
        Assert.Equal(13, clonedBuildingValues["Shadow1"]);
        Assert.Equal("Pine Walk", clonedBuildingValues["Shadow2"]);

        Assert.Equal("Building One", buildingValues["Name"]);
        Assert.Equal(11, buildingValues["Shadow1"]);
        Assert.Equal("Meadow Drive", buildingValues["Shadow2"]);
    }

    [ConditionalFact]
    public virtual void Using_bad_property_names_throws()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var entry = context.Entry(building);

        var buildingValues = entry.CurrentValues;
        var clonedBuildingValues = buildingValues.Clone();

        Assert.Equal(
            CoreStrings.PropertyNotFound("Foo", nameof(Building)),
            Assert.Throws<InvalidOperationException>(() => buildingValues["Foo"]).Message);

        Assert.Equal(
            CoreStrings.PropertyNotFound("Foo", nameof(Building)),
            Assert.Throws<InvalidOperationException>(() => clonedBuildingValues["Foo"]).Message);

        Assert.Equal(
            CoreStrings.PropertyNotFound("Foo", nameof(Building)),
            Assert.Throws<InvalidOperationException>(() => buildingValues["Foo"] = "foo").Message);

        Assert.Equal(
            CoreStrings.PropertyNotFound("Foo", nameof(Building)),
            Assert.Throws<InvalidOperationException>(() => clonedBuildingValues["Foo"] = "foo").Message);

        Assert.Equal(
            CoreStrings.PropertyNotFound("Foo", nameof(Building)),
            Assert.Throws<InvalidOperationException>(() => clonedBuildingValues.GetValue<string>("Foo")).Message);
    }

    [ConditionalFact]
    public virtual void Using_bad_IProperty_instances_throws()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var entry = context.Entry(building);

        var buildingValues = entry.CurrentValues;
        var clonedBuildingValues = buildingValues.Clone();

        var property = context.Model.FindEntityType(typeof(Whiteboard))!.FindProperty(nameof(Whiteboard.AssetTag))!;

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("AssetTag", nameof(Whiteboard), nameof(Building)),
            Assert.Throws<InvalidOperationException>(() => buildingValues[property]).Message);

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("AssetTag", nameof(Whiteboard), nameof(Building)),
            Assert.Throws<InvalidOperationException>(() => clonedBuildingValues[property]).Message);

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("AssetTag", nameof(Whiteboard), nameof(Building)),
            Assert.Throws<InvalidOperationException>(() => buildingValues[property] = "foo").Message);

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("AssetTag", nameof(Whiteboard), nameof(Building)),
            Assert.Throws<InvalidOperationException>(() => clonedBuildingValues[property] = "foo").Message);

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("AssetTag", nameof(Whiteboard), nameof(Building)),
            Assert.Throws<InvalidOperationException>(() => clonedBuildingValues.GetValue<string>(property)).Message);
    }

    [ConditionalFact]
    public virtual void Using_bad_property_names_throws_derived()
    {
        using var context = CreateContext();
        var employee = context.Set<Employee>().OfType<CurrentEmployee>().Single(b => b.FirstName == "Rowan");
        var entry = context.Entry(employee);

        var values = entry.CurrentValues;
        var clonedValues = values.Clone();

        Assert.Equal(
            CoreStrings.PropertyNotFound("Shadow4", nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => values["Shadow4"]).Message);

        Assert.Equal(
            CoreStrings.PropertyNotFound("Shadow4", nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => clonedValues["Shadow4"]).Message);

        Assert.Equal(
            CoreStrings.PropertyNotFound("Shadow4", nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => values["Shadow4"] = "foo").Message);

        Assert.Equal(
            CoreStrings.PropertyNotFound("Shadow4", nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => clonedValues["Shadow4"] = "foo").Message);

        Assert.Equal(
            CoreStrings.PropertyNotFound("TerminationDate", nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => values["TerminationDate"]).Message);

        Assert.Equal(
            CoreStrings.PropertyNotFound("TerminationDate", nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => clonedValues["TerminationDate"]).Message);

        Assert.Equal(
            CoreStrings.PropertyNotFound("TerminationDate", nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => values["TerminationDate"] = "foo").Message);

        Assert.Equal(
            CoreStrings.PropertyNotFound("TerminationDate", nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => clonedValues["TerminationDate"] = "foo").Message);

        Assert.Equal(
            CoreStrings.PropertyNotFound("Shadow4", nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => clonedValues.GetValue<string>("Shadow4")).Message);

        Assert.Equal(
            CoreStrings.PropertyNotFound("TerminationDate", nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => clonedValues.GetValue<string>("TerminationDate")).Message);
    }

    [ConditionalFact]
    public virtual void Using_bad_IProperty_instances_throws_derived()
    {
        using var context = CreateContext();
        var employee = context.Set<Employee>().OfType<CurrentEmployee>().Single(b => b.FirstName == "Rowan");
        var entry = context.Entry(employee);

        var values = entry.CurrentValues;
        var clonedValues = values.Clone();

        var shadowProperty = context.Model.FindEntityType(typeof(PastEmployee))!.FindProperty("Shadow4")!;
        var termProperty = context.Model.FindEntityType(typeof(PastEmployee))!.FindProperty(nameof(PastEmployee.TerminationDate))!;

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("Shadow4", nameof(PastEmployee), nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => values[shadowProperty]).Message);

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("Shadow4", nameof(PastEmployee), nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => clonedValues[shadowProperty]).Message);

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("Shadow4", nameof(PastEmployee), nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => values[shadowProperty] = "foo").Message);

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("Shadow4", nameof(PastEmployee), nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => clonedValues[shadowProperty] = "foo").Message);

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("Shadow4", nameof(PastEmployee), nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => clonedValues.GetValue<string>(shadowProperty)).Message);

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("TerminationDate", nameof(PastEmployee), nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => values[termProperty]).Message);

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("TerminationDate", nameof(PastEmployee), nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => clonedValues[termProperty]).Message);

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("TerminationDate", nameof(PastEmployee), nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => values[termProperty] = "foo").Message);

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("TerminationDate", nameof(PastEmployee), nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => clonedValues[termProperty] = "foo").Message);

        Assert.Equal(
            CoreStrings.PropertyDoesNotBelong("TerminationDate", nameof(PastEmployee), nameof(CurrentEmployee)),
            Assert.Throws<InvalidOperationException>(() => clonedValues.GetValue<string>(termProperty)).Message);
    }

    [ConditionalFact]
    public virtual void Using_non_collection_complex_property_throws()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var entry = context.Entry(building);

        var buildingValues = entry.CurrentValues;
        var clonedBuildingValues = buildingValues.Clone();

        var cultureProperty = context.Model.FindEntityType(typeof(Building))!.FindComplexProperty("Culture")!;

        Assert.Equal(
            CoreStrings.ComplexPropertyNotCollection("Building", "Culture"),
            Assert.Throws<InvalidOperationException>(() => buildingValues[cultureProperty]).Message);

        Assert.Equal(
            CoreStrings.ComplexPropertyNotCollection("Building", "Culture"),
            Assert.Throws<InvalidOperationException>(() => clonedBuildingValues[cultureProperty]).Message);

        Assert.Equal(
            CoreStrings.ComplexPropertyNotCollection("Building", "Culture"),
            Assert.Throws<InvalidOperationException>(() => buildingValues[cultureProperty] = null).Message);

        Assert.Equal(
            CoreStrings.ComplexPropertyNotCollection("Building", "Culture"),
            Assert.Throws<InvalidOperationException>(() => clonedBuildingValues[cultureProperty] = null).Message);

        Assert.Equal(
            CoreStrings.ComplexPropertyNotCollection("Building", "Culture"),
            Assert.Throws<InvalidOperationException>(() => clonedBuildingValues["Culture"]).Message);

        Assert.Equal(
            CoreStrings.ValueCannotBeNull("Culture", "Building", "Culture"),
            Assert.Throws<InvalidOperationException>(() => buildingValues["Culture"] = null).Message);

        Assert.Equal(
            CoreStrings.ComplexPropertyNotCollection("Building", "Culture"),
            Assert.Throws<InvalidOperationException>(() => clonedBuildingValues["Culture"] = null).Message);
    }

    [ConditionalFact]
    public virtual void Using_complex_property_value_not_list_throws()
    {
        using var context = CreateContext();
        //var school = context.Set<School>().Single();

        // Complex collection query support. Issue #31411
        var school = CreateSchool();
        context.Set<School>().Attach(school);
        
        var entry = context.Entry(school);

        var schoolValues = entry.CurrentValues;
        var clonedSchoolValues = schoolValues.Clone();

        var departmentsProperty = context.Model.FindEntityType(typeof(School))!.FindComplexProperty("Departments")!;

        Assert.Throws<InvalidCastException>(() => schoolValues[departmentsProperty] = new List<string>());

        Assert.Throws<InvalidCastException>(() => clonedSchoolValues[departmentsProperty] = new List<string> { "invalid" });

        Assert.Throws<InvalidCastException>(() => schoolValues["Departments"] = "invalid");

        Assert.Equal(
            CoreStrings.ComplexPropertyValueNotList("Departments", departmentsProperty.ClrType, "string"),
            Assert.Throws<InvalidOperationException>(() => clonedSchoolValues["Departments"] = "invalid").Message);

        Assert.Throws<InvalidCastException>(() => schoolValues["Departments"] = 123);

        Assert.Equal(
            CoreStrings.ComplexPropertyValueNotList("Departments", departmentsProperty.ClrType, "int"),
            Assert.Throws<InvalidOperationException>(() => clonedSchoolValues["Departments"] = 123).Message);
    }

    [ConditionalFact]
    public virtual Task Current_values_can_be_copied_into_a_non_generic_cloned_dictionary()
        => TestNonGenericPropertyValuesCloneToValues(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Original_values_can_be_copied_into_a_non_generic_cloned_dictionary()
        => TestNonGenericPropertyValuesCloneToValues(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_copied_into_a_non_generic_cloned_dictionary()
        => TestNonGenericPropertyValuesCloneToValues(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_copied_asynchronously_into_a_non_generic_cloned_dictionary()
        => TestNonGenericPropertyValuesCloneToValues(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestNonGenericPropertyValuesCloneToValues(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        building.Name = "Building One Prime";
        building.Value = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "The Avenue";

        var buildingValues = await getPropertyValues(context.Entry(building));
        var clonedBuildingValues = buildingValues.Clone();

        if (expectOriginalValues)
        {
            Assert.Equal("Building One", clonedBuildingValues["Name"]);
            Assert.Equal(1500000m, clonedBuildingValues["Value"]);
            Assert.Equal(11, clonedBuildingValues["Shadow1"]);
            Assert.Equal("Meadow Drive", clonedBuildingValues["Shadow2"]);
        }
        else
        {
            Assert.Equal("Building One Prime", clonedBuildingValues["Name"]);
            Assert.Equal(1500001m, clonedBuildingValues["Value"]);
            Assert.Equal(12, clonedBuildingValues["Shadow1"]);
            Assert.Equal("The Avenue", clonedBuildingValues["Shadow2"]);
        }

        // Test modification of cloned dictionaries does not impact original property values

        var newKey = new Guid();
        clonedBuildingValues["BuildingId"] = newKey; // Can change primary key on clone
        clonedBuildingValues["Name"] = "Building 18";
        clonedBuildingValues["Shadow1"] = 13;
        clonedBuildingValues["Shadow2"] = "Pine Walk";

        if (expectOriginalValues)
        {
            Assert.Equal(newKey, clonedBuildingValues["BuildingId"]);
            Assert.Equal("Building 18", clonedBuildingValues["Name"]);
            Assert.Equal(13, clonedBuildingValues["Shadow1"]);
            Assert.Equal("Pine Walk", clonedBuildingValues["Shadow2"]);

            Assert.Equal("Building One", buildingValues["Name"]);
            Assert.Equal(11, buildingValues["Shadow1"]);
            Assert.Equal("Meadow Drive", buildingValues["Shadow2"]);
        }
        else
        {
            Assert.Equal(newKey, clonedBuildingValues["BuildingId"]);
            Assert.Equal("Building 18", clonedBuildingValues["Name"]);
            Assert.Equal(13, clonedBuildingValues["Shadow1"]);
            Assert.Equal("Pine Walk", clonedBuildingValues["Shadow2"]);

            Assert.Equal("Building One Prime", buildingValues["Name"]);
            Assert.Equal(12, buildingValues["Shadow1"]);
            Assert.Equal("The Avenue", buildingValues["Shadow2"]);
        }
    }

    [ConditionalFact]
    public virtual Task Current_values_can_be_read_or_set_for_an_object_in_the_Deleted_state()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.CurrentValues), EntityState.Deleted, expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Original_values_can_be_read_and_set_for_an_object_in_the_Deleted_state()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.OriginalValues), EntityState.Deleted, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_read_and_set_for_an_object_in_the_Deleted_state()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.GetDatabaseValues()!), EntityState.Deleted, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_read_and_set_for_an_object_in_the_Deleted_state_asynchronously()
        => TestPropertyValuesPositiveForState(e => e.GetDatabaseValuesAsync()!, EntityState.Deleted, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Current_values_can_be_read_and_set_for_an_object_in_the_Unchanged_state()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.CurrentValues), EntityState.Unchanged, expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Original_values_can_be_read_and_set_for_an_object_in_the_Unchanged_state()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.OriginalValues), EntityState.Unchanged, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_read_and_set_for_an_object_in_the_Unchanged_state()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.GetDatabaseValues()!), EntityState.Unchanged, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_read_and_set_for_an_object_in_the_Unchanged_state_asynchronously()
        => TestPropertyValuesPositiveForState(e => e.GetDatabaseValuesAsync()!, EntityState.Unchanged, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Current_values_can_be_read_and_set_for_an_object_in_the_Modified_state()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.CurrentValues), EntityState.Modified, expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Original_values_can_be_read_and_set_for_an_object_in_the_Modified_state()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.OriginalValues), EntityState.Modified, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_read_and_set_for_an_object_in_the_Modified_state()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.GetDatabaseValues()!), EntityState.Modified, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_read_and_set_for_an_object_in_the_Modified_state_asynchronously()
        => TestPropertyValuesPositiveForState(e => e.GetDatabaseValuesAsync()!, EntityState.Modified, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Current_values_can_be_read_and_set_for_an_object_in_the_Added_state()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.CurrentValues), EntityState.Added, expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Original_values_can_be_read_or_set_for_an_object_in_the_Added_state()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.OriginalValues), EntityState.Added, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_read_or_set_for_an_object_in_the_Added_state()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.GetDatabaseValues()!), EntityState.Detached, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_read_or_set_for_an_object_in_the_Added_state_asynchronously()
        => TestPropertyValuesPositiveForState(e => e.GetDatabaseValuesAsync()!, EntityState.Detached, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Current_values_can_be_read_or_set_for_a_Detached_object()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.CurrentValues), EntityState.Detached, expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Original_values_can_be_read_or_set_for_a_Detached_object()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.OriginalValues), EntityState.Detached, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_read_or_set_for_a_Detached_object()
        => TestPropertyValuesPositiveForState(
            e => Task.FromResult(e.GetDatabaseValues()!), EntityState.Detached, expectOriginalValues: true);

    [ConditionalFact]
    public virtual Task Store_values_can_be_read_or_set_for_a_Detached_object_asynchronously()
        => TestPropertyValuesPositiveForState(e => e.GetDatabaseValuesAsync()!, EntityState.Detached, expectOriginalValues: true);

    private async Task TestPropertyValuesPositiveForState(
        Func<EntityEntry<Building>, Task<PropertyValues>> getPropertyValues,
        EntityState state,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var entry = context.Entry(building);
        entry.State = state;

        building.Name = "Building One Prime";

        var values = await getPropertyValues(entry);

        Assert.Equal(expectOriginalValues ? "Building One" : "Building One Prime", values["Name"]);

        values["Name"] = "Building One Optimal";

        Assert.Equal("Building One Optimal", values["Name"]);
    }

    [ConditionalTheory]
    [InlineData(EntityState.Unchanged, true)]
    [InlineData(EntityState.Unchanged, false)]
    [InlineData(EntityState.Modified, true)]
    [InlineData(EntityState.Modified, false)]
    [InlineData(EntityState.Added, true)]
    [InlineData(EntityState.Added, false)]
    [InlineData(EntityState.Deleted, true)]
    [InlineData(EntityState.Deleted, false)]
    [InlineData(EntityState.Detached, true)]
    [InlineData(EntityState.Detached, false)]
    public async Task Values_can_be_reloaded_from_database_for_entity_in_any_state(EntityState state, bool async)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var entry = context.Entry(building);

        entry.Property(e => e.Name).OriginalValue = "Original Building";
        building.Name = "Building One Prime";

        entry.State = state;

        if (async)
        {
            await entry.ReloadAsync();
        }
        else
        {
            entry.Reload();
        }

        Assert.Equal("Building One", entry.Property(e => e.Name).OriginalValue);
        Assert.Equal("Building One", entry.Property(e => e.Name).CurrentValue);
        Assert.Equal("Building One", building.Name);

        Assert.Equal(EntityState.Unchanged, entry.State);
    }

    [ConditionalTheory]
    [InlineData(EntityState.Unchanged, true)]
    [InlineData(EntityState.Unchanged, false)]
    [InlineData(EntityState.Modified, true)]
    [InlineData(EntityState.Modified, false)]
    [InlineData(EntityState.Added, true)]
    [InlineData(EntityState.Added, false)]
    [InlineData(EntityState.Deleted, true)]
    [InlineData(EntityState.Deleted, false)]
    [InlineData(EntityState.Detached, true)]
    [InlineData(EntityState.Detached, false)]
    public async Task Reload_when_entity_deleted_in_store_can_happen_for_any_state(EntityState state, bool async)
    {
        using var context = CreateContext();
        var office = new Office { Number = "35" };
        var mailRoom = new MailRoom { id = 36 };
        var building = Building.Create(Guid.NewGuid(), "Bag End", 77);

        building.Offices.Add(office);
        building.PrincipalMailRoom = mailRoom;
        office.Building = building;
        mailRoom.Building = building;

        var entry = context.Entry(building);

        context.Attach(building);
        entry.State = state;

        if (async)
        {
            await entry.ReloadAsync();
        }
        else
        {
            entry.Reload();
        }

        Assert.Equal("Bag End", entry.Property(e => e.Name).OriginalValue);
        Assert.Equal("Bag End", entry.Property(e => e.Name).CurrentValue);
        Assert.Equal("Bag End", building.Name);

        if (state == EntityState.Added)
        {
            Assert.Equal(EntityState.Added, entry.State);
            Assert.Same(mailRoom, building.PrincipalMailRoom);
            Assert.Contains(office, building.Offices);
        }
        else
        {
            Assert.Equal(EntityState.Detached, entry.State);
            Assert.Same(mailRoom, building.PrincipalMailRoom);
            Assert.Contains(office, building.Offices);

            Assert.Equal(EntityState.Detached, context.Entry(office.Building).State);
            Assert.Same(building, office.Building);
        }

        Assert.Same(mailRoom, building.PrincipalMailRoom);
        Assert.Contains(office, building.Offices);
    }

    [ConditionalTheory]
    [InlineData(EntityState.Unchanged, true)]
    [InlineData(EntityState.Unchanged, false)]
    [InlineData(EntityState.Modified, true)]
    [InlineData(EntityState.Modified, false)]
    [InlineData(EntityState.Added, true)]
    [InlineData(EntityState.Added, false)]
    [InlineData(EntityState.Deleted, true)]
    [InlineData(EntityState.Deleted, false)]
    [InlineData(EntityState.Detached, true)]
    [InlineData(EntityState.Detached, false)]
    public virtual async Task Values_can_be_reloaded_from_database_for_entity_in_any_state_with_inheritance(EntityState state, bool async)
    {
        using var context = CreateContext();
        var supplier = context.Set<Supplier33307>().Single();
        var customer = context.Set<Customer33307>().Single();

        supplier.Name = "X";
        supplier.Foo = "Z";
        customer.Name = "Y";
        customer.Bar = 77;
        customer.Address.Street = "New Road";
        supplier.Address.Street = "New Lane";

        context.Entry(supplier).State = state;
        context.Entry(customer).State = state;

        if (async)
        {
            await context.Entry(supplier).ReloadAsync();
            await context.Entry(customer).ReloadAsync();
        }
        else
        {
            context.Entry(supplier).Reload();
            context.Entry(customer).Reload();
        }

        Assert.Equal("Bar", customer.Name);
        Assert.Equal(11, customer.Bar);
        Assert.Equal("Two", customer.Address.Street);

        Assert.Equal("Foo", supplier.Name);
        Assert.Equal("F", supplier.Foo);
        Assert.Equal("One", supplier.Address.Street);
    }

    [ConditionalFact]
    public virtual void Current_values_can_be_set_from_an_object_using_generic_dictionary()
        => TestGenericObjectSetValues(e => e.CurrentValues, (e, n) => e.Property(n).CurrentValue!);

    [ConditionalFact]
    public virtual void Original_values_can_be_set_from_an_object_using_generic_dictionary()
        => TestGenericObjectSetValues(e => e.OriginalValues, (e, n) => e.Property(n).OriginalValue!);

    private void TestGenericObjectSetValues(
        Func<EntityEntry<Building>, PropertyValues> getPropertyValues,
        Func<EntityEntry, string, object> getValue)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var buildingValues = getPropertyValues(context.Entry(building));

        var newBuilding = Building.Create(
            new Guid(building.BuildingId.ToString()),
            "Values End",
            building.Value);

        buildingValues.SetValues(newBuilding);

        // Check Values

        Assert.Equal("Values End", buildingValues["Name"]);
        Assert.Equal(1500000m, buildingValues["Value"]);
        Assert.Equal(11, buildingValues["Shadow1"]);
        Assert.Equal("Meadow Drive", buildingValues["Shadow2"]);

        ValidateBuildingProperties(context.Entry(building), getValue, 11, "Meadow Drive");
    }

    private static void ValidateBuildingProperties(
        EntityEntry buildingEntry,
        Func<EntityEntry, string, object> getValue,
        int shadow1,
        string shadow2)
    {
        Assert.Equal("Values End", getValue(buildingEntry, "Name"));
        Assert.Equal(1500000m, getValue(buildingEntry, "Value"));
        Assert.Equal(shadow1, getValue(buildingEntry, "Shadow1"));
        Assert.Equal(shadow2, getValue(buildingEntry, "Shadow2"));

        Assert.True(buildingEntry.Property("Name").IsModified);
        Assert.False(buildingEntry.Property("BuildingId").IsModified);
        Assert.False(buildingEntry.Property("Value").IsModified);
        Assert.Equal(shadow1 != 11, buildingEntry.Property("Shadow1").IsModified);
        Assert.Equal(shadow2 != "Meadow Drive", buildingEntry.Property("Shadow2").IsModified);
    }

    [ConditionalFact]
    public virtual void Current_values_can_be_set_from_an_object_using_non_generic_dictionary()
        => TestNonGenericObjectSetValues(e => e.CurrentValues, (e, n) => e.Property(n).CurrentValue!);

    [ConditionalFact]
    public virtual void Original_values_can_be_set_from_an_object_using_non_generic_dictionary()
        => TestNonGenericObjectSetValues(e => e.OriginalValues, (e, n) => e.Property(n).OriginalValue!);

    private void TestNonGenericObjectSetValues(
        Func<EntityEntry, PropertyValues> getPropertyValues,
        Func<EntityEntry, string, object> getValue)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var buildingValues = getPropertyValues(context.Entry(building));

        var newBuilding = Building.Create(
            new Guid(building.BuildingId.ToString()),
            "Values End",
            building.Value);

        buildingValues.SetValues(newBuilding);

        Assert.Equal("Values End", buildingValues["Name"]);
        Assert.Equal(1500000m, buildingValues["Value"]);
        Assert.Equal(11, buildingValues["Shadow1"]);
        Assert.Equal("Meadow Drive", buildingValues["Shadow2"]);

        ValidateBuildingProperties(context.Entry(building), getValue, 11, "Meadow Drive");
    }

    [ConditionalFact]
    public virtual void Current_values_can_be_set_from_DTO_object_using_non_generic_dictionary()
        => TestNonGenericDtoSetValues(e => e.CurrentValues, (e, n) => e.Property(n).CurrentValue!);

    [ConditionalFact]
    public virtual void Original_values_can_be_set_from_DTO_object_using_non_generic_dictionary()
        => TestNonGenericDtoSetValues(e => e.OriginalValues, (e, n) => e.Property(n).OriginalValue!);

    private void TestNonGenericDtoSetValues(
        Func<EntityEntry, PropertyValues> getPropertyValues,
        Func<EntityEntry, string, object> getValue)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var buildingValues = getPropertyValues(context.Entry(building));

        var newBuilding = new BuildingDto
        {
            BuildingId = new Guid(building.BuildingId.ToString()),
            Name = "Values End",
            Value = building.Value,
            Shadow1 = 777
        };

        buildingValues.SetValues(newBuilding);

        // Check Values

        Assert.Equal("Values End", buildingValues["Name"]);
        Assert.Equal(1500000m, buildingValues["Value"]);
        Assert.Equal(777, buildingValues["Shadow1"]);
        Assert.Equal("Meadow Drive", buildingValues["Shadow2"]);

        ValidateBuildingProperties(context.Entry(building), getValue, 777, "Meadow Drive");
    }

    [ConditionalFact]
    public virtual void Current_values_can_be_set_from_DTO_object_missing_key_using_non_generic_dictionary()
        => TestNonGenericDtoNoKeySetValues(e => e.CurrentValues, (e, n) => e.Property(n).CurrentValue!);

    [ConditionalFact]
    public virtual void Original_values_can_be_set_from_DTO_object_missing_key_using_non_generic_dictionary()
        => TestNonGenericDtoNoKeySetValues(e => e.OriginalValues, (e, n) => e.Property(n).OriginalValue!);

    private void TestNonGenericDtoNoKeySetValues(
        Func<EntityEntry, PropertyValues> getPropertyValues,
        Func<EntityEntry, string, object> getValue)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var buildingValues = getPropertyValues(context.Entry(building));

        var newBuilding = new BuildingDtoNoKey
        {
            Name = "Values End",
            Value = building.Value,
            Shadow2 = "Cheese"
        };

        buildingValues.SetValues(newBuilding);

        // Check Values

        Assert.Equal("Values End", buildingValues["Name"]);
        Assert.Equal(1500000m, buildingValues["Value"]);
        Assert.Equal("Cheese", buildingValues["Shadow2"]);

        ValidateBuildingProperties(context.Entry(building), getValue, 11, "Cheese");
    }

    [ConditionalFact]
    public virtual void Current_values_can_be_set_from_dictionary()
        => TestDictionarySetValues(e => e.CurrentValues, (e, n) => e.Property(n).CurrentValue!);

    [ConditionalFact]
    public virtual void Original_values_can_be_set_from_dictionary()
        => TestDictionarySetValues(e => e.OriginalValues, (e, n) => e.Property(n).OriginalValue!);

    private void TestDictionarySetValues(
        Func<EntityEntry, PropertyValues> getPropertyValues,
        Func<EntityEntry, string, object> getValue)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var buildingValues = getPropertyValues(context.Entry(building));

        var dictionary = new Dictionary<string, object>
        {
            { "BuildingId", new Guid(building.BuildingId.ToString()) },
            { "Name", "Values End" },
            { "Value", building.Value },
            { "Shadow1", 13 },
            { "Shadow2", "Pine Walk" },
            { "PrincipalMailRoomId", 0 }
        };

        buildingValues.SetValues(dictionary);

        Assert.Equal("Values End", buildingValues["Name"]);
        Assert.Equal(1500000m, buildingValues["Value"]);
        Assert.Equal(13, buildingValues["Shadow1"]);
        Assert.Equal("Pine Walk", buildingValues["Shadow2"]);

        ValidateBuildingProperties(context.Entry(building), getValue, 13, "Pine Walk");
    }

    [ConditionalFact]
    public virtual void Current_values_can_be_set_from_dictionary_typed_int()
        => TestDictionarySetValuesTypedInt(e => e.CurrentValues, (e, n) => e.Property(n).CurrentValue!);

    [ConditionalFact]
    public virtual void Original_values_can_be_set_from_dictionary_typed_int()
        => TestDictionarySetValuesTypedInt(e => e.OriginalValues, (e, n) => e.Property(n).OriginalValue!);

    private void TestDictionarySetValuesTypedInt(
        Func<EntityEntry, PropertyValues> getPropertyValues,
        Func<EntityEntry, string, object> getValue)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var buildingValues = getPropertyValues(context.Entry(building));

        var dictionary = new Dictionary<string, int> { { "Shadow1", 13 }, { "PrincipalMailRoomId", 0 } };

        buildingValues.SetValues(dictionary);

        Assert.Equal("Building One", buildingValues["Name"]);
        Assert.Equal(1500000m, buildingValues["Value"]);
        Assert.Equal(13, buildingValues["Shadow1"]);
        Assert.Equal("Meadow Drive", buildingValues["Shadow2"]);

        Assert.Equal("Building One", getValue(context.Entry(building), "Name"));
        Assert.Equal(1500000m, getValue(context.Entry(building), "Value"));
        Assert.Equal(13, getValue(context.Entry(building), "Shadow1"));
        Assert.Equal("Meadow Drive", getValue(context.Entry(building), "Shadow2"));

        Assert.False(context.Entry(building).Property("Name").IsModified);
        Assert.False(context.Entry(building).Property("BuildingId").IsModified);
        Assert.False(context.Entry(building).Property("Value").IsModified);
        Assert.True(context.Entry(building).Property("Shadow1").IsModified);
        Assert.False(context.Entry(building).Property("Shadow2").IsModified);
    }

    [ConditionalFact]
    public virtual void Current_values_can_be_set_from_dictionary_typed_string()
        => TestDictionarySetValuesTypedString(e => e.CurrentValues, (e, n) => e.Property(n).CurrentValue!);

    [ConditionalFact]
    public virtual void Original_values_can_be_set_from_dictionary_typed_string()
        => TestDictionarySetValuesTypedString(e => e.OriginalValues, (e, n) => e.Property(n).OriginalValue!);

    private void TestDictionarySetValuesTypedString(
        Func<EntityEntry, PropertyValues> getPropertyValues,
        Func<EntityEntry, string, object> getValue)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var buildingValues = getPropertyValues(context.Entry(building));

        var dictionary = new Dictionary<string, string>
        {
            { "Name", "Values End" }, { "Shadow2", "Pine Walk" },
        };

        buildingValues.SetValues(dictionary);

        Assert.Equal("Values End", buildingValues["Name"]);
        Assert.Equal(1500000m, buildingValues["Value"]);
        Assert.Equal(11, buildingValues["Shadow1"]);
        Assert.Equal("Pine Walk", buildingValues["Shadow2"]);

        Assert.Equal("Values End", getValue(context.Entry(building), "Name"));
        Assert.Equal(1500000m, getValue(context.Entry(building), "Value"));
        Assert.Equal(11, getValue(context.Entry(building), "Shadow1"));
        Assert.Equal("Pine Walk", getValue(context.Entry(building), "Shadow2"));

        Assert.True(context.Entry(building).Property("Name").IsModified);
        Assert.False(context.Entry(building).Property("BuildingId").IsModified);
        Assert.False(context.Entry(building).Property("Value").IsModified);
        Assert.False(context.Entry(building).Property("Shadow1").IsModified);
        Assert.True(context.Entry(building).Property("Shadow2").IsModified);
    }

    [ConditionalFact]
    public virtual void Current_values_can_be_set_from_dictionary_some_missing()
        => TestPartialDictionarySetValues(e => e.CurrentValues, (e, n) => e.Property(n).CurrentValue!);

    [ConditionalFact]
    public virtual void Original_values_can_be_set_from_dictionary_some_missing()
        => TestPartialDictionarySetValues(e => e.OriginalValues, (e, n) => e.Property(n).OriginalValue!);

    private void TestPartialDictionarySetValues(
        Func<EntityEntry, PropertyValues> getPropertyValues,
        Func<EntityEntry, string, object> getValue)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var buildingValues = getPropertyValues(context.Entry(building));

        var dictionary = new Dictionary<string, object>
        {
            { "BuildingId", new Guid(building.BuildingId.ToString()) },
            { "Name", "Values End" },
            { "Value", building.Value },
            { "Shadow1", 777 }
        };

        buildingValues.SetValues(dictionary);

        // Check Values

        Assert.Equal("Values End", buildingValues["Name"]);
        Assert.Equal(1500000m, buildingValues["Value"]);
        Assert.Equal(777, buildingValues["Shadow1"]);
        Assert.Equal("Meadow Drive", buildingValues["Shadow2"]);

        ValidateBuildingProperties(context.Entry(building), getValue, 777, "Meadow Drive");
    }

    [ConditionalFact]
    public virtual void Current_values_can_be_set_from_one_generic_dictionary_to_another_generic_dictionary()
        => TestGenericValuesSetValues(e => e.CurrentValues, (e, n) => e.Property(n).CurrentValue!);

    [ConditionalFact]
    public virtual void Original_values_can_be_set_from_one_generic_dictionary_to_another_generic_dictionary()
        => TestGenericValuesSetValues(e => e.OriginalValues, (e, n) => e.Property(n).OriginalValue!);

    private void TestGenericValuesSetValues(
        Func<EntityEntry<Building>, PropertyValues> getPropertyValues,
        Func<EntityEntry, string, object> getValue)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var buildingValues = getPropertyValues(context.Entry(building));

        var clonedBuildingValues = buildingValues.Clone();

        clonedBuildingValues["BuildingId"] = new Guid(building.BuildingId.ToString());
        clonedBuildingValues["Name"] = "Values End";
        clonedBuildingValues["Value"] = building.Value;
        clonedBuildingValues["Shadow1"] = 13;
        clonedBuildingValues["Shadow2"] = "Pine Walk";

        buildingValues.SetValues(clonedBuildingValues);

        // Check Values

        Assert.Equal("Values End", buildingValues["Name"]);
        Assert.Equal(1500000m, buildingValues["Value"]);
        Assert.Equal(13, clonedBuildingValues["Shadow1"]);
        Assert.Equal("Pine Walk", clonedBuildingValues["Shadow2"]);

        ValidateBuildingProperties(context.Entry(building), getValue, 13, "Pine Walk");
    }

    [ConditionalFact]
    public virtual void Current_values_can_be_set_from_one_non_generic_dictionary_to_another_generic_dictionary()
        => TestNonGenericValuesSetValues(e => e.CurrentValues, (e, n) => e.Property(n).CurrentValue!);

    [ConditionalFact]
    public virtual void Original_values_can_be_set_from_one_non_generic_dictionary_to_another_generic_dictionary()
        => TestNonGenericValuesSetValues(e => e.OriginalValues, (e, n) => e.Property(n).OriginalValue!);

    private void TestNonGenericValuesSetValues(
        Func<EntityEntry, PropertyValues> getPropertyValues,
        Func<EntityEntry, string, object> getValue)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var buildingValues = getPropertyValues(context.Entry(building));

        var clonedBuildingValues = buildingValues.Clone();

        clonedBuildingValues["BuildingId"] = new Guid(building.BuildingId.ToString());
        clonedBuildingValues["Name"] = "Values End";
        clonedBuildingValues["Value"] = building.Value;
        clonedBuildingValues["Shadow1"] = 13;
        clonedBuildingValues["Shadow2"] = "Pine Walk";

        buildingValues.SetValues(clonedBuildingValues);

        // Check Values

        Assert.Equal("Values End", buildingValues["Name"]);
        Assert.Equal(1500000m, buildingValues["Value"]);
        Assert.Equal(13, buildingValues["Shadow1"]);
        Assert.Equal("Pine Walk", buildingValues["Shadow2"]);

        ValidateBuildingProperties(context.Entry(building), getValue, 13, "Pine Walk");
    }

    [ConditionalFact]
    public virtual void Primary_key_in_current_values_cannot_be_changed_in_property_dictionary()
        => TestKeyChange(e => e.CurrentValues);

    [ConditionalFact]
    public virtual void Primary_key_in_original_values_cannot_be_changed_in_property_dictionary()
        => TestKeyChange(e => e.OriginalValues);

    private void TestKeyChange(Func<EntityEntry<Building>, PropertyValues> getPropertyValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var values = getPropertyValues(context.Entry(building));

        Assert.Equal(
            CoreStrings.KeyReadOnly(nameof(Building.BuildingId), nameof(Building)),
            Assert.Throws<InvalidOperationException>(() => values["BuildingId"] = new Guid()).Message);
    }

    [ConditionalTheory]
    [InlineData(CascadeTiming.Immediate)]
    [InlineData(CascadeTiming.OnSaveChanges)]
    [InlineData(CascadeTiming.Never)]
    public virtual void Non_nullable_property_in_current_values_results_in_conceptual_null(CascadeTiming deleteOrphansTiming)
    {
        using var context = CreateContext();
        context.ChangeTracker.DeleteOrphansTiming = deleteOrphansTiming;

        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var entry = context.Entry(building);
        var values = entry.CurrentValues;
        var originalValue = values["Value"];

        Assert.False(entry.GetInfrastructure().HasConceptualNull);

        if (deleteOrphansTiming == CascadeTiming.Immediate)
        {
            if (context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.IsSensitiveDataLoggingEnabled)
            {
                Assert.Equal(
                    CoreStrings.PropertyConceptualNullSensitive(
                        "Value",
                        nameof(Building),
                        "{Value: " + Convert.ToString(originalValue, CultureInfo.InvariantCulture) + "}"),
                    Assert.Throws<InvalidOperationException>(() => values["Value"] = null).Message);
            }
            else
            {
                Assert.Equal(
                    CoreStrings.PropertyConceptualNull("Value", nameof(Building)),
                    Assert.Throws<InvalidOperationException>(() => values["Value"] = null).Message);
            }
        }
        else
        {
            values["Value"] = null;

            Assert.True(entry.GetInfrastructure().HasConceptualNull);

            Assert.Equal(1500000m, values["Value"]);
            Assert.Equal(1500000m, building.Value);
        }
    }

    [ConditionalTheory]
    [InlineData(CascadeTiming.Immediate)]
    [InlineData(CascadeTiming.OnSaveChanges)]
    [InlineData(CascadeTiming.Never)]
    public virtual void Non_nullable_shadow_property_in_current_values_results_in_conceptual_null(CascadeTiming deleteOrphansTiming)
    {
        using var context = CreateContext();
        context.ChangeTracker.DeleteOrphansTiming = deleteOrphansTiming;

        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var entry = context.Entry(building);
        var values = entry.CurrentValues;

        Assert.False(entry.GetInfrastructure().HasConceptualNull);

        if (deleteOrphansTiming == CascadeTiming.Immediate)
        {
            if (context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.IsSensitiveDataLoggingEnabled)
            {
                Assert.Equal(
                    CoreStrings.PropertyConceptualNullSensitive("Shadow1", nameof(Building), "{Shadow1: 11}"),
                    Assert.Throws<InvalidOperationException>(() => values["Shadow1"] = null).Message);
            }
            else
            {
                Assert.Equal(
                    CoreStrings.PropertyConceptualNull("Shadow1", nameof(Building)),
                    Assert.Throws<InvalidOperationException>(() => values["Shadow1"] = null).Message);
            }
        }
        else
        {
            values["Shadow1"] = null;

            Assert.True(entry.GetInfrastructure().HasConceptualNull);

            Assert.Equal(11, values["Shadow1"]);
        }
    }

    [ConditionalFact]
    public virtual void Non_nullable_property_in_original_values_cannot_be_set_to_null_in_property_dictionary()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var values = context.Entry(building).OriginalValues;

        Assert.Equal(
            CoreStrings.ValueCannotBeNull(nameof(Building.Value), nameof(Building), "decimal"),
            Assert.Throws<InvalidOperationException>(() => values["Value"] = null).Message);

        Assert.Equal(1500000m, values["Value"]);
    }

    [ConditionalFact]
    public virtual void Non_nullable_shadow_property_in_original_values_cannot_be_set_to_null_in_property_dictionary()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var values = context.Entry(building).OriginalValues;

        Assert.Equal(
            CoreStrings.ValueCannotBeNull("Shadow1", nameof(Building), "int"),
            Assert.Throws<InvalidOperationException>(() => values["Shadow1"] = null).Message);

        Assert.Equal(11, values["Shadow1"]);
    }

    [ConditionalFact]
    public virtual void Non_nullable_property_in_cloned_dictionary_cannot_be_set_to_null()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var values = context.Entry(building).CurrentValues.Clone();

        Assert.Equal(
            CoreStrings.ValueCannotBeNull(nameof(Building.Value), nameof(Building), "decimal"),
            Assert.Throws<InvalidOperationException>(() => values["Value"] = null).Message);
    }

    [ConditionalFact]
    public virtual void Property_in_current_values_cannot_be_set_to_instance_of_wrong_type()
        => TestSetWrongType(e => e.CurrentValues);

    [ConditionalFact]
    public virtual void Property_in_original_values_cannot_be_set_to_instance_of_wrong_type()
        => TestSetWrongType(e => e.OriginalValues);

    private void TestSetWrongType(Func<EntityEntry<Building>, PropertyValues> getPropertyValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var values = getPropertyValues(context.Entry(building));

        Assert.Throws<InvalidCastException>(() => values["Name"] = 1);

        Assert.Equal("Building One", values["Name"]);
        Assert.Equal("Building One", building.Name);
    }

    [ConditionalFact]
    public virtual void Shadow_property_in_current_values_cannot_be_set_to_instance_of_wrong_type()
        => TestSetWrongTypeShadow(e => e.CurrentValues);

    [ConditionalFact]
    public virtual void Shadow_property_in_original_values_cannot_be_set_to_instance_of_wrong_type()
        => TestSetWrongTypeShadow(e => e.OriginalValues);

    private void TestSetWrongTypeShadow(Func<EntityEntry<Building>, PropertyValues> getPropertyValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var values = getPropertyValues(context.Entry(building));

        Assert.Throws<InvalidCastException>(() => values["Shadow1"] = "foo");

        Assert.Equal(11, values["Shadow1"]);
    }

    [ConditionalFact]
    public virtual void Property_in_cloned_dictionary_cannot_be_set_to_instance_of_wrong_type()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var values = context.Entry(building).CurrentValues.Clone();

        Assert.Equal(
            CoreStrings.InvalidType(nameof(Building.Name), nameof(Building), "int", "string"),
            Assert.Throws<InvalidCastException>(() => values["Name"] = 1).Message);

        Assert.Equal("Building One", values["Name"]);
        Assert.Equal("Building One", building.Name);
    }

    [ConditionalFact]
    public virtual void Primary_key_in_current_values_cannot_be_changed_by_setting_values_from_object()
        => TestKeyChangeByObject(e => e.CurrentValues);

    [ConditionalFact]
    public virtual void Primary_key_in_original_values_cannot_be_changed_by_setting_values_from_object()
        => TestKeyChangeByObject(e => e.OriginalValues);

    private void TestKeyChangeByObject(Func<EntityEntry<Building>, PropertyValues> getPropertyValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var values = getPropertyValues(context.Entry(building));

        var newBuilding = (Building)values.ToObject();
        newBuilding.BuildingId = new Guid();

        Assert.Equal(
            CoreStrings.KeyReadOnly(nameof(Building.BuildingId), nameof(Building)),
            Assert.Throws<InvalidOperationException>(() => values.SetValues(newBuilding)).Message);
    }

    [ConditionalFact]
    public virtual void Primary_key_in_current_values_cannot_be_changed_by_setting_values_from_another_dictionary()
        => TestKeyChangeByValues(e => e.CurrentValues);

    [ConditionalFact]
    public virtual void Primary_key_in_original_values_cannot_be_changed_by_setting_values_from_another_dictionary()
        => TestKeyChangeByValues(e => e.OriginalValues);

    private void TestKeyChangeByValues(Func<EntityEntry<Building>, PropertyValues> getPropertyValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var values = getPropertyValues(context.Entry(building));

        var clone = values.Clone();
        clone["BuildingId"] = new Guid();

        Assert.Equal(
            CoreStrings.KeyReadOnly(nameof(Building.BuildingId), nameof(Building)),
            Assert.Throws<InvalidOperationException>(() => values.SetValues(clone)).Message);
    }

    [ConditionalFact]
    public virtual Task Properties_for_current_values_returns_properties()
        => TestProperties(e => Task.FromResult(e.CurrentValues));

    [ConditionalFact]
    public virtual Task Properties_for_original_values_returns_properties()
        => TestProperties(e => Task.FromResult(e.OriginalValues));

    [ConditionalFact]
    public virtual Task Properties_for_store_values_returns_properties()
        => TestProperties(e => Task.FromResult(e.GetDatabaseValues()!));

    [ConditionalFact]
    public virtual Task Properties_for_store_values_returns_properties_asynchronously()
        => TestProperties(e => e.GetDatabaseValuesAsync()!);

    [ConditionalFact]
    public virtual Task Properties_for_cloned_dictionary_returns_properties()
        => TestProperties(e => Task.FromResult(e.CurrentValues.Clone()));

    private async Task TestProperties(Func<EntityEntry<Building>, Task<PropertyValues>> getPropertyValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        var buildingValues = await getPropertyValues(context.Entry(building));
        var properties = buildingValues.Properties.Select(p => (p.DeclaringType.DisplayName(), p.Name)).ToList();

        if (context.Model.FindEntityType(typeof(Building))!.GetComplexProperties().Any())
        {
            Assert.Equal(
                [
                    ("Building", "BuildingId"),
                    ("Building", "Name"),
                    ("Building", "PrincipalMailRoomId"),
                    ("Building", "Shadow1"),
                    ("Building", "Shadow2"),
                    ("Building", "Value"),
                    ("Building.Culture#Culture", "Rating"),
                    ("Building.Culture#Culture", "Species"),
                    ("Building.Culture#Culture", "Subspecies"),
                    ("Building.Culture#Culture", "Validation"),
                    ("Building.Culture#Culture.License#License", "Charge"),
                    ("Building.Culture#Culture.License#License", "Title"),
                    ("Building.Culture#Culture.License#License.Tag#Tag", "Text"),
                    ("Building.Culture#Culture.License#License.Tog#Tog", "Text"),
                    ("Building.Culture#Culture.Manufacturer#Manufacturer", "Name"),
                    ("Building.Culture#Culture.Manufacturer#Manufacturer", "Rating"),
                    ("Building.Culture#Culture.Manufacturer#Manufacturer.Tag#Tag", "Text"),
                    ("Building.Culture#Culture.Manufacturer#Manufacturer.Tog#Tog", "Text"),
                    ("Building.Milk#Milk", "Rating"),
                    ("Building.Milk#Milk", "Species"),
                    ("Building.Milk#Milk", "Subspecies"),
                    ("Building.Milk#Milk", "Validation"),
                    ("Building.Milk#Milk.License#License", "Charge"),
                    ("Building.Milk#Milk.License#License", "Title"),
                    ("Building.Milk#Milk.License#License.Tag#Tag", "Text"),
                    ("Building.Milk#Milk.License#License.Tog#Tog", "Text"),
                    ("Building.Milk#Milk.Manufacturer#Manufacturer", "Name"),
                    ("Building.Milk#Milk.Manufacturer#Manufacturer", "Rating"),
                    ("Building.Milk#Milk.Manufacturer#Manufacturer.Tag#Tag", "Text"),
                    ("Building.Milk#Milk.Manufacturer#Manufacturer.Tog#Tog", "Text"),
                ],
                properties);
        }
        else
        {
            Assert.Equal(
                [
                    ("Building", "BuildingId"),
                    ("Building", "Name"),
                    ("Building", "PrincipalMailRoomId"),
                    ("Building", "Shadow1"),
                    ("Building", "Shadow2"),
                    ("Building", "Value"),
                ],
                properties);
        }
    }

    [ConditionalFact]
    public virtual Task GetDatabaseValues_for_entity_not_in_the_store_returns_null()
        => GetDatabaseValues_for_entity_not_in_the_store_returns_null_implementation(e => Task.FromResult(e.GetDatabaseValues()!));

    [ConditionalFact]
    public virtual Task GetDatabaseValuesAsync_for_entity_not_in_the_store_returns_null()
        => GetDatabaseValues_for_entity_not_in_the_store_returns_null_implementation(e => e.GetDatabaseValuesAsync()!);

    private async Task GetDatabaseValues_for_entity_not_in_the_store_returns_null_implementation(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues)
    {
        using var context = CreateContext();
        var building = (Building)context.Entry(
            context.Set<Building>().Single(b => b.Name == "Building One")).CurrentValues.ToObject();

        building.BuildingId = new Guid();

        context.Set<Building>().Attach(building);

        Assert.Null(await getPropertyValues(context.Entry(building)));
    }

    [ConditionalFact]
    public virtual Task NonGeneric_GetDatabaseValues_for_entity_not_in_the_store_returns_null()
        => NonGeneric_GetDatabaseValues_for_entity_not_in_the_store_returns_null_implementation(
            e => Task.FromResult(e.GetDatabaseValues()!));

    [ConditionalFact]
    public virtual Task NonGeneric_GetDatabaseValuesAsync_for_entity_not_in_the_store_returns_null()
        => NonGeneric_GetDatabaseValues_for_entity_not_in_the_store_returns_null_implementation(e => e.GetDatabaseValuesAsync()!);

    private async Task NonGeneric_GetDatabaseValues_for_entity_not_in_the_store_returns_null_implementation(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues)
    {
        using var context = CreateContext();
        var building =
            (Building)
            context.Entry(context.Set<Building>().Single(b => b.Name == "Building One")).CurrentValues.ToObject();
        building.BuildingId = new Guid();

        context.Set<Building>().Attach(building);

        Assert.Null(await getPropertyValues(context.Entry((object)building)));

        Assert.True(building.CreatedCalled);
        Assert.True(building.InitializingCalled);
        Assert.True(building.InitializedCalled);
    }

    [ConditionalFact]
    public virtual Task GetDatabaseValues_for_derived_entity_not_in_the_store_returns_null()
        => GetDatabaseValues_for_derived_entity_not_in_the_store_returns_null_implementation(
            e => Task.FromResult(e.GetDatabaseValues()!));

    [ConditionalFact]
    public virtual Task GetDatabaseValuesAsync_for_derived_entity_not_in_the_store_returns_null()
        => GetDatabaseValues_for_derived_entity_not_in_the_store_returns_null_implementation(e => e.GetDatabaseValuesAsync()!);

    private async Task GetDatabaseValues_for_derived_entity_not_in_the_store_returns_null_implementation(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues)
    {
        using var context = CreateContext();
        var employee = (CurrentEmployee)context.Entry(
                context.Set<Employee>()
                    .OfType<CurrentEmployee>()
                    .Single(b => b.FirstName == "Rowan"))
            .CurrentValues
            .ToObject();
        employee.EmployeeId = -77;

        context.Set<Employee>().Attach(employee);

        Assert.Null(await getPropertyValues(context.Entry(employee)));
    }

    [ConditionalFact]
    public virtual Task NonGeneric_GetDatabaseValues_for_derived_entity_not_in_the_store_returns_null()
        => NonGeneric_GetDatabaseValues_for_derived_entity_not_in_the_store_returns_null_implementation(
            e => Task.FromResult(e.GetDatabaseValues()!));

    [ConditionalFact]
    public virtual Task NonGeneric_GetDatabaseValuesAsync_for_derived_entity_not_in_the_store_returns_null()
        => NonGeneric_GetDatabaseValues_for_derived_entity_not_in_the_store_returns_null_implementation(
            e => e.GetDatabaseValuesAsync()!);

    private async Task NonGeneric_GetDatabaseValues_for_derived_entity_not_in_the_store_returns_null_implementation(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues)
    {
        using var context = CreateContext();
        var employee = (CurrentEmployee)context.Entry(
                context.Set<Employee>()
                    .OfType<CurrentEmployee>()
                    .Single(b => b.FirstName == "Rowan"))
            .CurrentValues
            .ToObject();
        employee.EmployeeId = -77;

        context.Set<Employee>().Attach(employee);

        Assert.Null(await getPropertyValues(context.Entry((object)employee)));
    }

    [ConditionalFact]
    public virtual Task GetDatabaseValues_for_the_wrong_type_in_the_store_returns_null()
        => GetDatabaseValues_for_the_wrong_type_in_the_store_returns_null_implementation(
            e => Task.FromResult(e.GetDatabaseValues()!));

    [ConditionalFact]
    public virtual Task GetDatabaseValuesAsync_for_the_wrong_type_in_the_store_returns_null()
        => GetDatabaseValues_for_the_wrong_type_in_the_store_returns_null_implementation(e => e.GetDatabaseValuesAsync()!);

    private async Task GetDatabaseValues_for_the_wrong_type_in_the_store_returns_null_implementation(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues)
    {
        using var context = CreateContext();
        var pastEmployeeId = context.Set<Employee>()
            .OfType<PastEmployee>()
            .AsNoTracking()
            .OrderBy(e => e.EmployeeId)
            .FirstOrDefault()!
            .EmployeeId;

        var employee = (CurrentEmployee)context.Entry(
                context.Set<Employee>()
                    .OfType<CurrentEmployee>()
                    .Single(b => b.FirstName == "Rowan"))
            .CurrentValues
            .ToObject();
        employee.EmployeeId = pastEmployeeId;

        context.Set<Employee>().Attach(employee);

        Assert.Null(await getPropertyValues(context.Entry(employee)));
    }

    [ConditionalFact]
    public virtual Task NonGeneric_GetDatabaseValues_for_the_wrong_type_in_the_store_throws()
        => NonGeneric_GetDatabaseValues_for_the_wrong_type_in_the_store_throws_implementation(
            e => Task.FromResult(e.GetDatabaseValues()!));

    [ConditionalFact]
    public virtual Task NonGeneric_GetDatabaseValuesAsync_for_the_wrong_type_in_the_store_throws()
        => NonGeneric_GetDatabaseValues_for_the_wrong_type_in_the_store_throws_implementation(e => e.GetDatabaseValuesAsync()!);

    private async Task NonGeneric_GetDatabaseValues_for_the_wrong_type_in_the_store_throws_implementation(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues)
    {
        using var context = CreateContext();
        var pastEmployeeId = context.Set<Employee>()
            .OfType<PastEmployee>()
            .AsNoTracking()
            .OrderBy(e => e.EmployeeId)
            .FirstOrDefault()!
            .EmployeeId;

        var employee = (CurrentEmployee)context.Entry(
                context.Set<Employee>()
                    .OfType<CurrentEmployee>()
                    .Single(b => b.FirstName == "Rowan"))
            .CurrentValues
            .ToObject();
        employee.EmployeeId = pastEmployeeId;

        context.Set<Employee>().Attach(employee);

        Assert.Null(await getPropertyValues(context.Entry((object)employee)));
    }

    [ConditionalFact]
    public Task Store_values_really_are_store_values_not_current_or_original_values()
        => Store_values_really_are_store_values_not_current_or_original_values_implementation(
            e => Task.FromResult(e.GetDatabaseValues()!));

    [ConditionalFact]
    public Task Store_values_really_are_store_values_not_current_or_original_values_async()
        => Store_values_really_are_store_values_not_current_or_original_values_implementation(e => e.GetDatabaseValuesAsync()!);

    private async Task Store_values_really_are_store_values_not_current_or_original_values_implementation(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");
        building.Name = "Values End";

        context.Entry(building).State = EntityState.Unchanged;

        var storeValues = (Building)(await getPropertyValues(context.Entry(building))).ToObject();

        Assert.Equal("Building One", storeValues.Name);
    }

    [ConditionalFact]
    public virtual void Setting_store_values_does_not_change_current_or_original_values()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        var storeValues = context.Entry(building).GetDatabaseValues()!;
        storeValues["Name"] = "Bag End";

        var currentValues = (Building)context.Entry(building).CurrentValues.ToObject();
        Assert.Equal("Building One", currentValues.Name);

        Assert.True(currentValues.CreatedCalled);
        Assert.True(currentValues.InitializingCalled);
        Assert.True(currentValues.InitializedCalled);

        var originalValues = (Building)context.Entry(building).OriginalValues.ToObject();
        Assert.Equal("Building One", originalValues.Name);

        Assert.True(originalValues.CreatedCalled);
        Assert.True(originalValues.InitializingCalled);
        Assert.True(originalValues.InitializedCalled);
    }

    [ConditionalFact]
    public virtual Task Complex_collection_current_values_can_be_accessed_as_a_property_dictionary()
        => TestComplexCollectionPropertyValues(e => Task.FromResult(e.CurrentValues), expectOriginalValues: false);

    [ConditionalFact]
    public virtual Task Complex_collection_original_values_can_be_accessed_as_a_property_dictionary()
        => TestComplexCollectionPropertyValues(e => Task.FromResult(e.OriginalValues), expectOriginalValues: true);

    [ConditionalFact(Skip = "Complex collection query support. Issue #31411")]
    public virtual Task Complex_collection_store_values_can_be_accessed_as_a_property_dictionary()
        => TestComplexCollectionPropertyValues(e => Task.FromResult(e.GetDatabaseValues()!), expectOriginalValues: true);

    [ConditionalFact(Skip = "Complex collection query support. Issue #31411")]
    public virtual Task Complex_collection_store_values_can_be_accessed_asynchronously_as_a_property_dictionary()
        => TestComplexCollectionPropertyValues(e => e.GetDatabaseValuesAsync()!, expectOriginalValues: true);

    private async Task TestComplexCollectionPropertyValues(
        Func<EntityEntry<School>, Task<PropertyValues>> getPropertyValues,
        bool expectOriginalValues)
    {
        using var context = CreateContext();
        //var school = context.Set<School>().Single(s => s.Name == "Test School");

        // Complex collection query support. Issue #31411
        var school = CreateSchool();
        context.Set<School>().Attach(school);

        var originalDepartments = school.Departments.ToList();
        var originalFirstDepartmentCourses = school.Departments.First().Courses.ToList();

        school.Departments.Clear();
        school.Departments.Add(new Department
        {
            Name = "Modified Department",
            Building = "Modified Building",
            Courses =
            [
                new Course { Name = "Modified Course 1", Credits = 4 },
                new Course { Name = "Modified Course 2", Credits = 5 }
            ]
        });

        var entry = context.Entry(school);
        var values = await getPropertyValues(entry);

        Assert.Equal("Test School", values["Name"]);
        Assert.Equal("Test School", values[entry.Property(e => e.Name).Metadata]);
        Assert.Equal(1, values[entry.Property(e => e.Id).Metadata]);

        var departmentsComplexProperty = entry.Metadata.FindComplexProperty(nameof(School.Departments))!;
        if (expectOriginalValues)
        {
            Assert.Equal("Test School", values["Name"]);
            var departments = (IList<Department>)values["Departments"]!;
            var departmentsViaComplexProperty = (IList<Department>)values[departmentsComplexProperty]!;
            Assert.Equal(2, departments.Count);
            Assert.Equal(2, departmentsViaComplexProperty.Count);

            var dept1 = departments[0];
            var dept1Object = departmentsViaComplexProperty[0];
            Assert.Equal("Computer Science", dept1.Name);
            Assert.Equal("Building A", dept1.Building);
            Assert.Equal("Computer Science", dept1Object.Name);
            Assert.Equal("Building A", dept1Object.Building);


            var department1Courses = dept1.Courses;
            Assert.Equal(2, department1Courses.Count);

            Assert.Equal("Data Structures", department1Courses[0].Name);
            Assert.Equal(3, department1Courses[0].Credits);
            Assert.Equal("Data Structures", department1Courses[0].Name);
            Assert.Equal(3, department1Courses[0].Credits);

            Assert.Equal("Algorithms", department1Courses[1].Name);
            Assert.Equal(4, department1Courses[1].Credits);
            Assert.Equal("Algorithms", department1Courses[1].Name);
            Assert.Equal(4, department1Courses[1].Credits);

            var dept2 = departments[1];
            Assert.Equal("Mathematics", dept2.Name);
            Assert.Equal("Building B", dept2.Building);
            Assert.Equal("Mathematics", dept2.Name);
            Assert.Equal("Building B", dept2.Building);

            var department2Courses = dept2.Courses;
            Assert.Equal(2, department2Courses.Count);
            Assert.Equal("Calculus I", department2Courses[0].Name);
            Assert.Equal(4, department2Courses[0].Credits);
            Assert.Equal("Linear Algebra", department2Courses[1].Name);
            Assert.Equal(3, department2Courses[1].Credits);
        }
        else
        {
            Assert.Equal("Test School", values["Name"]);
            var departments = (IList<Department>)values["Departments"]!;
            var departmentsViaComplexProperty = (IList<Department>)values[departmentsComplexProperty]!;

            Assert.Single(departments);
            Assert.Single(departmentsViaComplexProperty);
            var dept = departments[0];
            var deptViaComplexProperty = departmentsViaComplexProperty[0];
            Assert.Equal("Modified Department", dept.Name);
            Assert.Equal("Modified Building", dept.Building);
            Assert.Equal("Modified Department", deptViaComplexProperty.Name);
            Assert.Equal("Modified Building", deptViaComplexProperty.Building);

            Assert.Equal("Modified Department", dept.Name);
            Assert.Equal("Modified Building", dept.Building);

            var courses = dept.Courses;
            Assert.Equal(2, courses.Count);

            Assert.Equal("Modified Course 1", courses[0].Name);
            Assert.Equal(4, courses[0].Credits);
            Assert.Equal("Modified Course 1", courses[0].Name);
            Assert.Equal(4, courses[0].Credits);

            Assert.Equal("Modified Course 2", courses[1].Name);
            Assert.Equal(5, courses[1].Credits);
            Assert.Equal("Modified Course 2", courses[1].Name);
            Assert.Equal(5, courses[1].Credits);
        }

        Assert.False(school.CreatedCalled);
        Assert.False(school.InitializingCalled);
        Assert.False(school.InitializedCalled);
    }

    [ConditionalFact]
    public virtual void Setting_complex_collection_values_from_object_works()
    {
        using var context = CreateContext();
        //var school = context.Set<School>().Single(s => s.Name == "Test School");

        // Complex collection query support. Issue #31411
        var school = CreateSchool();
        context.Set<School>().Attach(school);

        var newSchool = new School
        {
            Id = school.Id,
            Name = "Completely New School",
            Departments =
            [
                new Department
                {
                    Name = "New Department",
                    Building = "New Building",
                    Courses =
                    [
                        new Course { Name = "New Course", Credits = 4 }
                    ]
                }
            ]
        };
        var entry = context.Entry(school);
        entry.CurrentValues.SetValues(newSchool);

        Assert.Equal("Completely New School", school.Name);
        Assert.Single(school.Departments);

        var department = school.Departments[0];
        Assert.Equal("New Department", department.Name);
        Assert.Equal("New Building", department.Building);
        Assert.Single(department.Courses);
        var course = department.Courses[0];
        Assert.Equal("New Course", course.Name);

        Assert.Equal(4, course.Credits);

        var internalEntry = entry.GetInfrastructure();
        var departmentsProperty = entry.Metadata.FindComplexProperty(nameof(School.Departments))!;
        var departmentEntry = internalEntry.GetComplexCollectionEntry(departmentsProperty, 0);

        Assert.Equal(EntityState.Modified, departmentEntry.EntityState);
        Assert.Equal(EntityState.Deleted, internalEntry.GetComplexCollectionOriginalEntry(departmentsProperty, 1).EntityState);

        var coursesProperty = departmentsProperty.ComplexType.FindComplexProperty(nameof(Department.Courses))!;
        var courseEntries = departmentEntry.GetComplexCollectionEntries(coursesProperty);

        Assert.Single(courseEntries);
        Assert.Equal(EntityState.Modified, courseEntries[0]!.EntityState);
    }

    [ConditionalFact]
    public virtual void Setting_complex_collection_current_values_from_object_with_nulls_works()
    {
        using var context = CreateContext();
        //var school = context.Set<School>().Single(s => s.Name == "Test School");

        // Complex collection query support. Issue #31411
        var school = CreateSchool();
        context.Set<School>().Attach(school);

        var newSchool = new School
        {
            Id = school.Id,
            Name = "School with Nulls",
            Departments =
            [
                new Department
                {
                    Name = "Department 1",
                    Building = "Building 1",
                    Courses = [null!, new Course { Name = "Course A", Credits = 3 }, null!]
                },
                null!,
                new Department
                {
                    Name = "Department 2",
                    Building = "Building 2",
                    Courses = [new Course { Name = "Course B", Credits = 4 }]
                }
            ]
        };

        var entry = context.Entry(school);
        entry.CurrentValues.SetValues(newSchool);

        Assert.Equal("School with Nulls", school.Name);
        Assert.Equal(3, school.Departments.Count);

        Assert.Equal("Department 1", school.Departments[0]!.Name);
        Assert.Equal("Building 1", school.Departments[0]!.Building);
        Assert.Equal(3, school.Departments[0]!.Courses.Count);
        Assert.Null(school.Departments[0]!.Courses[0]);
        Assert.Equal("Course A", school.Departments[0]!.Courses[1]!.Name);
        Assert.Equal(3, school.Departments[0]!.Courses[1]!.Credits);
        Assert.Null(school.Departments[0]!.Courses[2]);
        Assert.Null(school.Departments[1]);

        Assert.Equal("Department 2", school.Departments[2]!.Name);
        Assert.Equal("Building 2", school.Departments[2]!.Building);
        Assert.Single(school.Departments[2]!.Courses);
        Assert.Equal("Course B", school.Departments[2]!.Courses[0]!.Name);
        Assert.Equal(4, school.Departments[2]!.Courses[0]!.Credits);

        var currentValues = entry.CurrentValues;
        var departments = (IList<Department>)currentValues["Departments"]!;
        Assert.Equal(3, departments.Count);

        Assert.Equal("Department 1", departments[0].Name);
        Assert.Equal("Building 1", departments[0].Building);
        var dept1Courses = departments[0].Courses;
        Assert.Equal(3, dept1Courses.Count);
        Assert.Null(dept1Courses[0]);
        Assert.Equal("Course A", dept1Courses[1].Name);
        Assert.Equal(3, dept1Courses[1].Credits);
        Assert.Null(dept1Courses[2]);
        Assert.Null(departments[1]);

        Assert.Equal("Department 2", departments[2].Name);
        Assert.Equal("Building 2", departments[2].Building);
        var dept2Courses = departments[2].Courses;
        Assert.Single(dept2Courses);
        Assert.Equal("Course B", dept2Courses[0].Name);
        Assert.Equal(4, dept2Courses[0].Credits);

        var internalEntry = entry.GetInfrastructure();
        var departmentsProperty = entry.Metadata.FindComplexProperty(nameof(School.Departments))!;
        var departmentEntries = internalEntry.GetComplexCollectionEntries(departmentsProperty);

        Assert.Equal(3, departmentEntries.Count);
        Assert.Equal(EntityState.Modified, departmentEntries[0]!.EntityState);
        Assert.Equal(EntityState.Added, departmentEntries[1]!.EntityState);
        Assert.Equal(EntityState.Modified, departmentEntries[2]!.EntityState);

        var coursesProperty = departmentsProperty.ComplexType.FindComplexProperty(nameof(Department.Courses))!;

        var dept1CourseEntries = departmentEntries[0]!.GetComplexCollectionEntries(coursesProperty);
        Assert.Equal(3, dept1CourseEntries.Count);
        if (dept1CourseEntries[0]!.EntityState == EntityState.Added)
        {
            Assert.Equal(EntityState.Modified, dept1CourseEntries[1]!.EntityState);
            Assert.Equal(EntityState.Modified, dept1CourseEntries[2]!.EntityState);
        }
        else
        {
            Assert.Equal(EntityState.Modified, dept1CourseEntries[0]!.EntityState);
            Assert.Equal(EntityState.Modified, dept1CourseEntries[1]!.EntityState);
            Assert.Equal(EntityState.Added, dept1CourseEntries[2]!.EntityState);
        }

        var dept2CourseEntries = departmentEntries[2]!.GetComplexCollectionEntries(coursesProperty);
        Assert.Single(dept2CourseEntries);
        Assert.Equal(EntityState.Modified, dept2CourseEntries[0]!.EntityState);
    }

    [ConditionalFact]
    public virtual void Setting_complex_collection_original_values_from_object_with_nulls_works()
    {
        using var context = CreateContext();
        //var school = context.Set<School>().Single(s => s.Name == "Test School");

        // Complex collection query support. Issue #31411
        var school = CreateSchool();
        context.Set<School>().Attach(school);

        var newSchool = new School
        {
            Id = school.Id,
            Name = "Original School with Nulls",
            Departments =
            [
                null!,
                new Department
                {
                    Name = "Original Department",
                    Building = "Original Building",
                    Courses = [new Course { Name = "Original Course", Credits = 2 }, null!]
                }
            ]
        };

        var entry = context.Entry(school);
        entry.OriginalValues.SetValues(newSchool);

        Assert.Equal("Test School", school.Name);
        Assert.Equal(2, school.Departments.Count);

        var originalValues = entry.OriginalValues;
        Assert.Equal("Original School with Nulls", originalValues["Name"]);
        var originalDepartments = (IList<Department>)originalValues["Departments"]!;
        Assert.Equal(2, originalDepartments.Count);

        Assert.Null(originalDepartments[0]);
        Assert.Equal("Original Department", originalDepartments[1].Name);
        Assert.Equal("Original Building", originalDepartments[1].Building);

        var originalCourses = originalDepartments[1].Courses;
        Assert.Equal(2, originalCourses.Count);
        Assert.Equal("Original Course", originalCourses[0].Name);
        Assert.Equal(2, originalCourses[0].Credits);
        Assert.Null(originalCourses[1]);

        var internalEntry = entry.GetInfrastructure();
        var departmentsProperty = entry.Metadata.FindComplexProperty(nameof(School.Departments))!;
        var departmentEntries = internalEntry.GetComplexCollectionEntries(departmentsProperty);

        Assert.Equal(2, departmentEntries.Count);
        Assert.Equal(EntityState.Modified, departmentEntries[0]!.EntityState);
        Assert.Equal(EntityState.Modified, departmentEntries[1]!.EntityState);

        var coursesProperty = departmentsProperty.ComplexType.FindComplexProperty(nameof(Department.Courses))!;

        var dept1CourseEntries = departmentEntries[0]!.GetComplexCollectionEntries(coursesProperty);
        Assert.Equal(2, dept1CourseEntries.Count);
        Assert.Equal(EntityState.Modified, dept1CourseEntries[0]!.EntityState);
        Assert.Equal(EntityState.Modified, dept1CourseEntries[1]!.EntityState);

        var dept2CourseEntries = departmentEntries[1]!.GetComplexCollectionEntries(coursesProperty);
        Assert.Equal(2, dept2CourseEntries.Count);
        Assert.Equal(EntityState.Modified, dept2CourseEntries[0]!.EntityState);
        Assert.Equal(EntityState.Modified, dept2CourseEntries[1]!.EntityState);
    }

    [ConditionalFact]
    public virtual void Setting_complex_collection_current_values_from_dictionary_works()
    {
        using var context = CreateContext();
        //var school = context.Set<School>().Single(s => s.Name == "Test School");

        // Complex collection query support. Issue #31411
        var school = CreateSchool();
        context.Set<School>().Attach(school);

        var dictionary = new Dictionary<string, object>
        {
            ["Id"] = school.Id,
            ["Name"] = "Dictionary School",
            ["Departments"] = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["Name"] = "Dict Department 1",
                    ["Building"] = "Dict Building 1",
                    ["Courses"] = new List<Dictionary<string, object>>
                    {
                        new() { ["Name"] = "Dict Course 1", ["Credits"] = 5 },
                        new() { ["Name"] = "Dict Course 2", ["Credits"] = 6 }
                    }
                },
                new()
                {
                    ["Name"] = "Dict Department 2",
                    ["Building"] = "Dict Building 2",
                    ["Courses"] = new List<Dictionary<string, object>>
                    {
                        new() { ["Name"] = "Dict Course 3", ["Credits"] = 7 }
                    }
                }
            }
        };

        var entry = context.Entry(school);
        entry.CurrentValues.SetValues(dictionary);

        Assert.Equal("Dictionary School", school.Name);
        Assert.Equal(2, school.Departments.Count);

        Assert.Equal("Dict Department 1", school.Departments[0].Name);
        Assert.Equal("Dict Building 1", school.Departments[0].Building);
        Assert.Equal(2, school.Departments[0].Courses.Count);
        Assert.Equal("Dict Course 1", school.Departments[0].Courses[0].Name);
        Assert.Equal(5, school.Departments[0].Courses[0].Credits);
        Assert.Equal("Dict Course 2", school.Departments[0].Courses[1].Name);
        Assert.Equal(6, school.Departments[0].Courses[1].Credits);

        Assert.Equal("Dict Department 2", school.Departments[1].Name);
        Assert.Equal("Dict Building 2", school.Departments[1].Building);
        Assert.Single(school.Departments[1].Courses);
        Assert.Equal("Dict Course 3", school.Departments[1].Courses[0].Name);
        Assert.Equal(7, school.Departments[1].Courses[0].Credits);

        var currentValues = entry.CurrentValues;
        var departments = (IList<Department>)currentValues["Departments"]!;
        Assert.Equal(2, departments.Count);
        Assert.Equal("Dict Department 1", departments[0].Name);
        Assert.Equal("Dict Building 1", departments[0].Building);
        var dept1Courses = departments[0].Courses;
        Assert.Equal(2, dept1Courses.Count);
        Assert.Equal("Dict Course 1", dept1Courses[0].Name);
        Assert.Equal(5, dept1Courses[0].Credits);

        var internalEntry = entry.GetInfrastructure();
        var departmentsProperty = entry.Metadata.FindComplexProperty(nameof(School.Departments))!;
        var departmentEntries = internalEntry.GetComplexCollectionEntries(departmentsProperty);

        Assert.Equal(2, departmentEntries.Count);
        Assert.Equal(EntityState.Modified, departmentEntries[0]!.EntityState);
        Assert.Equal(EntityState.Modified, departmentEntries[1]!.EntityState);

        var coursesProperty = departmentsProperty.ComplexType.FindComplexProperty(nameof(Department.Courses))!;

        var dept1CourseEntries = departmentEntries[0]!.GetComplexCollectionEntries(coursesProperty);
        Assert.Equal(2, dept1CourseEntries.Count);
        Assert.Equal(EntityState.Modified, dept1CourseEntries[0]!.EntityState);
        Assert.Equal(EntityState.Modified, dept1CourseEntries[1]!.EntityState);

        var dept2CourseEntries = departmentEntries[1]!.GetComplexCollectionEntries(coursesProperty);
        Assert.Single(dept2CourseEntries);
        Assert.Equal(EntityState.Modified, dept2CourseEntries[0]!.EntityState);
    }

    [ConditionalFact]
    public virtual void Setting_complex_collection_values_from_DTO_with_nulls_works()
    {
        using var context = CreateContext();
        //var school = context.Set<School>().Single(s => s.Name == "Test School");

        // Complex collection query support. Issue #31411
        var school = CreateSchool();
        context.Set<School>().Attach(school);

        var dto = new SchoolDto
        {
            Id = school.Id,
            Name = "DTO School",
            Departments = new List<DepartmentDto?>
            {
                new()
                {
                    Name = "DTO Department",
                    Building = "DTO Building",
                    Courses = new List<CourseDto?>
                    {
                        null,
                        new() { Name = "DTO Course", Credits = 8 }
                    }
                },
                null
            }
        };

        var entry = context.Entry(school);
        entry.CurrentValues.SetValues(dto);

        Assert.Equal("DTO School", school.Name);
        Assert.Equal(2, school.Departments.Count);

        Assert.Equal("DTO Department", school.Departments[0]!.Name);
        Assert.Equal("DTO Building", school.Departments[0]!.Building);
        Assert.Equal(2, school.Departments[0]!.Courses.Count);
        Assert.Null(school.Departments[0]!.Courses[0]);
        Assert.Equal("DTO Course", school.Departments[0]!.Courses[1]!.Name);
        Assert.Equal(8, school.Departments[0]!.Courses[1]!.Credits);

        Assert.Null(school.Departments[1]);

        var currentValues = entry.CurrentValues;
        var departments = (IList<Department>)currentValues["Departments"]!;
        Assert.Equal(2, departments.Count);
        Assert.Equal("DTO Department", departments[0].Name);
        var courses = departments[0].Courses;
        Assert.Equal(2, courses.Count);
        Assert.Null(courses[0]);
        Assert.Equal("DTO Course", courses[1].Name);
        Assert.Equal(8, courses[1].Credits);
        Assert.Null(departments[1]);

        var internalEntry = entry.GetInfrastructure();
        var departmentsProperty = entry.Metadata.FindComplexProperty(nameof(School.Departments))!;
        var departmentEntries = internalEntry.GetComplexCollectionEntries(departmentsProperty);

        Assert.Equal(2, departmentEntries.Count);
        Assert.Equal(EntityState.Modified, departmentEntries[0]!.EntityState);
        Assert.Equal(EntityState.Modified, departmentEntries[1]!.EntityState);

        var coursesProperty = departmentsProperty.ComplexType.FindComplexProperty(nameof(Department.Courses))!;

        var dept1CourseEntries = departmentEntries[0]!.GetComplexCollectionEntries(coursesProperty);
        Assert.Equal(2, dept1CourseEntries.Count);
        Assert.Equal(EntityState.Modified, dept1CourseEntries[0]!.EntityState);
        Assert.Equal(EntityState.Modified, dept1CourseEntries[1]!.EntityState);
    }

    [ConditionalFact]
    public virtual void Setting_complex_collection_current_values_from_dictionary_with_nulls_works()
    {
        using var context = CreateContext();
        //var school = context.Set<School>().Single(s => s.Name == "Test School");

        // Complex collection query support. Issue #31411
        var school = CreateSchool();
        context.Set<School>().Attach(school);

        var dictionary = new Dictionary<string, object>
        {
            ["Id"] = school.Id,
            ["Name"] = "Dictionary School with Nulls",
            ["Departments"] = new List<Dictionary<string, object>?>
            {
                null,
                new()
                {
                    ["Name"] = "Dict Department",
                    ["Building"] = "Dict Building",
                    ["Courses"] = new List<Dictionary<string, object>?>
                    {
                        new() { ["Name"] = "Dict Course 1", ["Credits"] = 5 },
                        null,
                        new() { ["Name"] = "Dict Course 2", ["Credits"] = 6 }
                    }
                }
            }
        };

        var entry = context.Entry(school);
        entry.CurrentValues.SetValues(dictionary);

        // Verify entity state
        Assert.Equal("Dictionary School with Nulls", school.Name);
        Assert.Equal(2, school.Departments.Count);
        Assert.Null(school.Departments[0]);
        Assert.Equal("Dict Department", school.Departments[1]!.Name);
        Assert.Equal("Dict Building", school.Departments[1]!.Building);
        Assert.Equal(3, school.Departments[1]!.Courses.Count);
        Assert.Equal("Dict Course 1", school.Departments[1]!.Courses[0]!.Name);
        Assert.Equal(5, school.Departments[1]!.Courses[0]!.Credits);
        Assert.Null(school.Departments[1]!.Courses[1]);
        Assert.Equal("Dict Course 2", school.Departments[1]!.Courses[2]!.Name);
        Assert.Equal(6, school.Departments[1]!.Courses[2]!.Credits);

        var currentValues = entry.CurrentValues;
        var departmentsComplexProperty = entry.Metadata.FindComplexProperty(nameof(School.Departments))!;

        var departments = (IList<Department>)currentValues["Departments"]!;
        var departmentsViaComplexProperty = (IList<Department>)currentValues[departmentsComplexProperty]!;

        Assert.Equal(2, departments.Count);
        Assert.Equal(2, departmentsViaComplexProperty.Count);
        Assert.Null(departments[0]);
        Assert.Null(departmentsViaComplexProperty[0]);

        var deptNameProperty = departmentsComplexProperty.ComplexType.FindProperty(nameof(Department.Name))!;
        var deptBuildingProperty = departmentsComplexProperty.ComplexType.FindProperty(nameof(Department.Building))!;

        Assert.Equal("Dict Department", departments[1].Name);
        Assert.Equal("Dict Building", departments[1].Building);
        Assert.Equal("Dict Department", departments[1].Name);
        Assert.Equal("Dict Building", departments[1].Building);

        var courses = departments[1].Courses;
        var coursesComplexProperty = departmentsComplexProperty.ComplexType.FindComplexProperty(nameof(Department.Courses))!;
        var courseNameProperty = coursesComplexProperty.ComplexType.FindProperty(nameof(Course.Name))!;
        var courseCreditsProperty = coursesComplexProperty.ComplexType.FindProperty(nameof(Course.Credits))!;

        Assert.Equal(3, courses.Count);
        Assert.Equal("Dict Course 1", courses[0].Name);
        Assert.Equal(5, courses[0].Credits);
        Assert.Equal("Dict Course 1", courses[0].Name);
        Assert.Equal(5, courses[0].Credits);
        Assert.Null(courses[1]);
        Assert.Equal("Dict Course 2", courses[2].Name);
        Assert.Equal(6, courses[2].Credits);
        Assert.Equal("Dict Course 2", courses[2].Name);
        Assert.Equal(6, courses[2].Credits);

        var internalEntry = entry.GetInfrastructure();
        var departmentEntries = internalEntry.GetComplexCollectionEntries(departmentsComplexProperty);

        Assert.Equal(2, departmentEntries.Count);
        Assert.Equal(EntityState.Modified, departmentEntries[0]!.EntityState);
        Assert.Equal(EntityState.Modified, departmentEntries[1]!.EntityState);

        var dept2CourseEntries = departmentEntries[1]!.GetComplexCollectionEntries(coursesComplexProperty);
        Assert.Equal(3, dept2CourseEntries.Count);
        Assert.Equal(EntityState.Modified, dept2CourseEntries[0]!.EntityState);
        Assert.Equal(EntityState.Added, dept2CourseEntries[1]!.EntityState);
        Assert.Equal(EntityState.Modified, dept2CourseEntries[2]!.EntityState);
    }

    [ConditionalFact]
    public virtual void Setting_complex_collection_original_values_from_dictionary_with_nulls_works()
    {
        using var context = CreateContext();
        //var school = context.Set<School>().Single(s => s.Name == "Test School");

        // Complex collection query support. Issue #31411
        var school = CreateSchool();
        context.Set<School>().Attach(school);

        var dictionary = new Dictionary<string, object>
        {
            ["Id"] = school.Id,
            ["Name"] = "Original Dictionary School",
            ["Departments"] = new List<Dictionary<string, object>?>
            {
                new()
                {
                    ["Name"] = "Original Dict Department",
                    ["Building"] = "Original Dict Building",
                    ["Courses"] = new List<Dictionary<string, object>?>
                    {
                        null,
                        new() { ["Name"] = "Original Dict Course", ["Credits"] = 2 }
                    }
                },
                null
            }
        };

        var entry = context.Entry(school);
        entry.OriginalValues.SetValues(dictionary);

        Assert.Equal("Test School", school.Name);
        Assert.Equal(2, school.Departments.Count);

        var originalValues = entry.OriginalValues;
        var departmentsComplexProperty = entry.Metadata.FindComplexProperty(nameof(School.Departments))!;

        Assert.Equal("Original Dictionary School", originalValues["Name"]);
        var originalDepartments = (IList<Department>)originalValues["Departments"]!;
        var originalDepartmentsViaComplexProperty = (IList<Department>)originalValues[departmentsComplexProperty]!;

        Assert.Equal(2, originalDepartments.Count);
        Assert.Equal(2, originalDepartmentsViaComplexProperty.Count);

        var deptNameProperty = departmentsComplexProperty.ComplexType.FindProperty(nameof(Department.Name))!;
        var deptBuildingProperty = departmentsComplexProperty.ComplexType.FindProperty(nameof(Department.Building))!;

        Assert.Equal("Original Dict Department", originalDepartments[0].Name);
        Assert.Equal("Original Dict Building", originalDepartments[0].Building);
        Assert.Equal("Original Dict Department", originalDepartments[0].Name);
        Assert.Equal("Original Dict Building", originalDepartments[0].Building);
        Assert.Null(originalDepartments[1]);
        Assert.Null(originalDepartmentsViaComplexProperty[1]);

        var originalCourses = originalDepartments[0].Courses;
        var coursesComplexProperty = departmentsComplexProperty.ComplexType.FindComplexProperty(nameof(Department.Courses))!;
        var courseNameProperty = coursesComplexProperty.ComplexType.FindProperty(nameof(Course.Name))!;
        var courseCreditsProperty = coursesComplexProperty.ComplexType.FindProperty(nameof(Course.Credits))!;

        Assert.Equal(2, originalCourses.Count);
        Assert.Null(originalCourses[0]);
        Assert.Equal("Original Dict Course", originalCourses[1].Name);
        Assert.Equal(2, originalCourses[1].Credits);
        Assert.Equal("Original Dict Course", originalCourses[1].Name);
        Assert.Equal(2, originalCourses[1].Credits);

        var internalEntry = entry.GetInfrastructure();
        var departmentEntries = internalEntry.GetComplexCollectionEntries(departmentsComplexProperty);

        Assert.Equal(2, departmentEntries.Count);
        Assert.Equal(EntityState.Modified, departmentEntries[0]!.EntityState);
        Assert.Equal(EntityState.Modified, departmentEntries[1]!.EntityState);

        var dept1CourseEntries = departmentEntries[0]!.GetComplexCollectionEntries(coursesComplexProperty);
        Assert.Equal(2, dept1CourseEntries.Count);
        Assert.Equal(EntityState.Modified, dept1CourseEntries[0]!.EntityState);
        Assert.Equal(EntityState.Modified, dept1CourseEntries[1]!.EntityState);

        var dept2CourseEntries = departmentEntries[1]!.GetComplexCollectionEntries(coursesComplexProperty);
        Assert.Equal(2, dept2CourseEntries.Count);
        Assert.Equal(EntityState.Modified, dept2CourseEntries[0]!.EntityState);
        Assert.Equal(EntityState.Modified, dept2CourseEntries[1]!.EntityState);
    }

    [ConditionalFact]
    public virtual void Setting_complex_collection_current_values_from_DTO_with_complex_metadata_access_works()
    {
        using var context = CreateContext();
        //var school = context.Set<School>().Single(s => s.Name == "Test School");

        // Complex collection query support. Issue #31411
        var school = CreateSchool();
        context.Set<School>().Attach(school);

        var dto = new SchoolDto
        {
            Id = school.Id,
            Name = "Advanced DTO School",
            Departments = new List<DepartmentDto?>
            {
                new()
                {
                    Name = "Advanced DTO Department 1",
                    Building = "Advanced DTO Building 1",
                    Courses = new List<CourseDto?>
                    {
                        new() { Name = "Advanced DTO Course 1", Credits = 10 },
                        null,
                        new() { Name = "Advanced DTO Course 2", Credits = 12 }
                    }
                },
                null,
                new()
                {
                    Name = "Advanced DTO Department 2",
                    Building = "Advanced DTO Building 2",
                    Courses = new List<CourseDto?>
                    {
                        new() { Name = "Advanced DTO Course 3", Credits = 15 }
                    }
                }
            }
        };

        var entry = context.Entry(school);
        entry.CurrentValues.SetValues(dto);

        Assert.Equal("Advanced DTO School", school.Name);
        Assert.Equal(3, school.Departments.Count);

        var currentValues = entry.CurrentValues;
        var departmentsComplexProperty = entry.Metadata.FindComplexProperty(nameof(School.Departments))!;

        var departments = (IList<Department>)currentValues[departmentsComplexProperty]!;
        Assert.Equal(3, departments.Count);

        var deptNameProperty = departmentsComplexProperty.ComplexType.FindProperty(nameof(Department.Name))!;
        var deptBuildingProperty = departmentsComplexProperty.ComplexType.FindProperty(nameof(Department.Building))!;

        Assert.Equal("Advanced DTO Department 1", departments[0].Name);
        Assert.Equal("Advanced DTO Building 1", departments[0].Building);

        var dept1Courses = departments[0].Courses;
        var coursesComplexProperty = departmentsComplexProperty.ComplexType.FindComplexProperty(nameof(Department.Courses))!;
        var courseNameProperty = coursesComplexProperty.ComplexType.FindProperty(nameof(Course.Name))!;
        var courseCreditsProperty = coursesComplexProperty.ComplexType.FindProperty(nameof(Course.Credits))!;

        Assert.Equal(3, dept1Courses.Count);
        Assert.Equal("Advanced DTO Course 1", dept1Courses[0].Name);
        Assert.Equal(10, dept1Courses[0].Credits);
        Assert.Null(dept1Courses[1]);
        Assert.Equal("Advanced DTO Course 2", dept1Courses[2].Name);
        Assert.Equal(12, dept1Courses[2].Credits);

        Assert.Null(departments[1]);

        Assert.Equal("Advanced DTO Department 2", departments[2].Name);
        Assert.Equal("Advanced DTO Building 2", departments[2].Building);

        var dept2Courses = departments[2].Courses;
        Assert.Single(dept2Courses);
        Assert.Equal("Advanced DTO Course 3", dept2Courses[0].Name);
        Assert.Equal(15, dept2Courses[0].Credits);

        var internalEntry = entry.GetInfrastructure();
        var departmentEntries = internalEntry.GetComplexCollectionEntries(departmentsComplexProperty);

        Assert.Equal(3, departmentEntries.Count);
        Assert.Equal(EntityState.Modified, departmentEntries[0]!.EntityState);
        Assert.Equal(EntityState.Added, departmentEntries[1]!.EntityState);
        Assert.Equal(EntityState.Modified, departmentEntries[2]!.EntityState);

        var dept1CourseEntries = departmentEntries[0]!.GetComplexCollectionEntries(coursesComplexProperty);
        Assert.Equal(3, dept1CourseEntries.Count);
        Assert.Equal(EntityState.Modified, dept1CourseEntries[0]!.EntityState);
        Assert.Equal(EntityState.Added, dept1CourseEntries[1]!.EntityState);
        Assert.Equal(EntityState.Modified, dept1CourseEntries[2]!.EntityState);

        var dept3CourseEntries = departmentEntries[2]!.GetComplexCollectionEntries(coursesComplexProperty);
        Assert.Single(dept3CourseEntries);
        Assert.Equal(EntityState.Modified, dept3CourseEntries[0]!.EntityState);
    }

    [ConditionalFact]
    public virtual void SetValues_throws_for_complex_collection_with_non_list_value()
    {
        using var context = CreateContext();
        var school = CreateSchool();
        context.Set<School>().Attach(school);

        var dictionary = new Dictionary<string, object>
        {
            ["Name"] = "Test School",
            ["Departments"] = "Not a list"
        };

        var entry = context.Entry(school);
        var exception = Assert.Throws<InvalidOperationException>(() => entry.OriginalValues.SetValues(dictionary));
        Assert.Equal(CoreStrings.ComplexCollectionValueNotDictionaryList("Departments", "string"), exception.Message);
    }

    [ConditionalFact]
    public virtual void SetValues_throws_for_complex_collection_with_non_dictionary_item()
    {
        using var context = CreateContext();
        var school = CreateSchool();
        context.Set<School>().Attach(school);

        var dictionary = new Dictionary<string, object>
        {
            ["Name"] = "Test School",
            ["Departments"] = new List<object?> { "Not a dictionary", null }
        };

        var entry = context.Entry(school);
        var exception = Assert.Throws<InvalidOperationException>(() => entry.OriginalValues.SetValues(dictionary));
        Assert.Equal(CoreStrings.ComplexCollectionValueNotDictionaryList("Departments", "string"), exception.Message);
    }

    [ConditionalFact]
    public virtual void SetValues_throws_for_nested_complex_collection_with_non_list_value()
    {
        using var context = CreateContext();
        var school = CreateSchool();
        context.Set<School>().Attach(school);

        var dictionary = new Dictionary<string, object>
        {
            ["Name"] = "Test School",
            ["Departments"] = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["Name"] = "Department 1",
                    ["Building"] = "Building 1",
                    ["Courses"] = "Not a list"
                }
            }
        };

        var entry = context.Entry(school);
        var exception = Assert.Throws<InvalidOperationException>(() => entry.OriginalValues.SetValues(dictionary));
        Assert.Equal(CoreStrings.ComplexCollectionValueNotDictionaryList("Courses", "string"), exception.Message);
    }

    [ConditionalFact]
    public virtual void SetValues_throws_for_nested_complex_collection_with_non_dictionary_item()
    {
        using var context = CreateContext();
        var school = CreateSchool();
        context.Set<School>().Attach(school);

        var dictionary = new Dictionary<string, object>
        {
            ["Name"] = "Test School",
            ["Departments"] = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["Name"] = "Department 1",
                    ["Building"] = "Building 1",
                    ["Courses"] = new List<object?> { "Not a dictionary" }
                }
            }
        };

        var entry = context.Entry(school);
        var exception = Assert.Throws<InvalidOperationException>(() => entry.OriginalValues.SetValues(dictionary));
        Assert.Equal(CoreStrings.ComplexCollectionValueNotDictionaryList("Courses", "List<object>"), exception.Message);
    }

    [ConditionalFact]
    public virtual void SetValues_throws_for_complex_property_with_non_dictionary_value()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        var dictionary = new Dictionary<string, object>
        {
            ["Name"] = "Building",
            ["Culture"] = "Not a dictionary"
        };

        var entry = context.Entry(building);
        var exception = Assert.Throws<InvalidOperationException>(() => entry.OriginalValues.SetValues(dictionary));
        Assert.Equal(CoreStrings.ComplexPropertyValueNotDictionary("Culture", "string"), exception.Message);
    }

    [ConditionalFact]
    public virtual void Current_values_can_be_copied_to_object_using_ToObject()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        building.Name = "Building One Prime";
        building.Value = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "Pine Walk";

        var values = context.Entry(building).CurrentValues;
        var copy = (Building)values.ToObject();

        Assert.Equal("Building One Prime", copy.Name);
        Assert.Equal(1500001m, copy.Value);
        Assert.Equal(building.BuildingId, copy.BuildingId);

        Assert.True(copy.CreatedCalled);
        Assert.True(copy.InitializingCalled);
        Assert.True(copy.InitializedCalled);

        if (context.Model.FindEntityType(typeof(School)) != null)
        {
            //var school = context.Set<School>().First();

            // Complex collection query support. Issue #31411
            var school = CreateSchool();
            context.Set<School>().Attach(school);
            school.Name = "Modified School";
            school.Departments[0].Name = "Modified Department";
            school.Departments[0].Courses[0].Name = "Modified Course";
            school.Departments[0].Courses[0].Credits = 999;

            var schoolValues = context.Entry(school).CurrentValues;
            var schoolCopy = (School)schoolValues.ToObject();

            Assert.Equal("Modified School", schoolCopy.Name);
            Assert.Equal(school.Id, schoolCopy.Id);
            Assert.Equal(school.Departments.Count, schoolCopy.Departments.Count);
            Assert.Equal("Modified Department", schoolCopy.Departments[0].Name);
            Assert.Equal(school.Departments[0].Courses.Count, schoolCopy.Departments[0].Courses.Count);
            Assert.Equal("Modified Course", schoolCopy.Departments[0].Courses[0].Name);
            Assert.Equal(999, schoolCopy.Departments[0].Courses[0].Credits);
        }
    }

    [ConditionalFact]
    public virtual void Original_values_can_be_copied_to_object_using_ToObject()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        building.Name = "Building One Prime";
        building.Value = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "Pine Walk";

        var values = context.Entry(building).OriginalValues;
        var copy = (Building)values.ToObject();

        Assert.Equal("Building One", copy.Name);
        Assert.Equal(1500000m, copy.Value);
        Assert.Equal(building.BuildingId, copy.BuildingId);

        Assert.True(copy.CreatedCalled);
        Assert.True(copy.InitializingCalled);
        Assert.True(copy.InitializedCalled);

        if (context.Model.FindEntityType(typeof(School)) != null)
        {
            //var school = context.Set<School>().First();

            // Complex collection query support. Issue #31411
            var school = CreateSchool();
            context.Set<School>().Attach(school);
            school.Name = "Modified School";
            school.Departments[0].Name = "Modified Department";
            school.Departments[0].Courses[0].Name = "Modified Course";
            school.Departments[0].Courses[0].Credits = 999;

            var schoolValues = context.Entry(school).OriginalValues;
            var schoolCopy = (School)schoolValues.ToObject();

            Assert.Equal("Test School", schoolCopy.Name);
            Assert.Equal(school.Id, schoolCopy.Id);
            Assert.Equal(2, schoolCopy.Departments.Count);
            Assert.Equal("Computer Science", schoolCopy.Departments[0].Name);
            Assert.Equal(2, schoolCopy.Departments[0].Courses.Count);
            Assert.Equal("Data Structures", schoolCopy.Departments[0].Courses[0].Name);
            Assert.Equal(3, schoolCopy.Departments[0].Courses[0].Credits);
        }
    }

    [ConditionalFact]
    public virtual Task Store_values_can_be_copied_to_object_using_ToObject()
        => Store_values_can_be_copied_to_object_using_ToObject_implementation(e => Task.FromResult(e.GetDatabaseValues()!));

    [ConditionalFact]
    public virtual Task Store_values_can_be_copied_to_object_using_ToObject_asynchronously()
        => Store_values_can_be_copied_to_object_using_ToObject_implementation(e => e.GetDatabaseValuesAsync()!);

    private async Task Store_values_can_be_copied_to_object_using_ToObject_implementation(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        building.Name = "Building One Prime";
        building.Value = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "Pine Walk";

        var values = await getPropertyValues(context.Entry(building));
        var copy = (Building)values.ToObject();

        Assert.Equal("Building One", copy.Name);
        Assert.Equal(1500000m, copy.Value);
        Assert.Equal(building.BuildingId, copy.BuildingId);

        Assert.True(copy.CreatedCalled);
        Assert.True(copy.InitializingCalled);
        Assert.True(copy.InitializedCalled);

        if (context.Model.FindEntityType(typeof(School)) != null)
        {
            //var school = context.Set<School>().First();

            // Complex collection query support. Issue #31411
            var school = CreateSchool();
            context.Set<School>().Attach(school);
            school.Name = "Modified School";
            school.Departments[0].Name = "Modified Department";
            school.Departments[0].Courses[0].Name = "Modified Course";
            school.Departments[0].Courses[0].Credits = 999;

            var schoolValues = await getPropertyValues(context.Entry(school));
            if (schoolValues != null)
            {
                var schoolCopy = (School)schoolValues.ToObject();

                Assert.Equal("Test School", schoolCopy.Name);
                Assert.Equal(school.Id, schoolCopy.Id);
                Assert.Equal(2, schoolCopy.Departments.Count);
                Assert.Equal("Computer Science", schoolCopy.Departments[0].Name);
                Assert.Equal(2, schoolCopy.Departments[0].Courses.Count);
                Assert.Equal("Data Structures", schoolCopy.Departments[0].Courses[0].Name);
                Assert.Equal(3, schoolCopy.Departments[0].Courses[0].Credits);
            }
        }
    }

    [ConditionalFact]
    public virtual void Current_values_can_be_cloned()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        building.Name = "Building One Prime";
        building.Value = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "Pine Walk";

        var values = context.Entry(building).CurrentValues;
        var clone = values.Clone();

        Assert.NotSame(values, clone);
        Assert.Equal("Building One Prime", clone["Name"]);
        Assert.Equal(1500001m, clone["Value"]);
        Assert.Equal(12, clone["Shadow1"]);
        Assert.Equal("Pine Walk", clone["Shadow2"]);

        values["Name"] = "Modified";
        Assert.Equal("Building One Prime", clone["Name"]);

        if (context.Model.FindEntityType(typeof(School)) != null)
        {
            //var school = context.Set<School>().First();

            // Complex collection query support. Issue #31411
            var school = CreateSchool();
            context.Set<School>().Attach(school);
            school.Name = "Modified School";
            school.Departments[0].Name = "Modified Department";
            school.Departments[0].Courses[0].Name = "Modified Course";
            school.Departments[0].Courses[0].Credits = 999;

            var schoolValues = context.Entry(school).CurrentValues;
            var schoolClone = schoolValues.Clone();

            Assert.NotSame(schoolValues, schoolClone);
            Assert.Equal("Modified School", schoolClone["Name"]);
            Assert.Equal(school.Id, schoolClone["Id"]);

            schoolValues["Name"] = "Further Modified";
            Assert.Equal("Modified School", schoolClone["Name"]);

            var departmentsProperty = schoolValues.ComplexCollectionProperties.Single(p => p.Name == "Departments");
            var originalDepts = schoolValues[departmentsProperty];
            var clonedDepts = schoolClone[departmentsProperty];
            Assert.NotSame(originalDepts, clonedDepts);
        }
    }

    [ConditionalFact]
    public virtual void Original_values_can_be_cloned()
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        building.Name = "Building One Prime";
        building.Value = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "Pine Walk";

        var values = context.Entry(building).OriginalValues;
        var clone = values.Clone();

        Assert.NotSame(values, clone);
        Assert.Equal("Building One", clone["Name"]);
        Assert.Equal(1500000m, clone["Value"]);
        Assert.Equal(11, clone["Shadow1"]);
        Assert.Equal("Meadow Drive", clone["Shadow2"]);

        values["Name"] = "Modified";
        Assert.Equal("Building One", clone["Name"]);

        if (context.Model.FindEntityType(typeof(School)) != null)
        {
            //var school = context.Set<School>().First();

            // Complex collection query support. Issue #31411
            var school = CreateSchool();
            context.Set<School>().Attach(school);
            school.Name = "Modified School";
            school.Departments[0].Name = "Modified Department";
            school.Departments[0].Courses[0].Name = "Modified Course";
            school.Departments[0].Courses[0].Credits = 999;

            var schoolValues = context.Entry(school).OriginalValues;
            var schoolClone = schoolValues.Clone();

            Assert.NotSame(schoolValues, schoolClone);
            Assert.Equal("Test School", schoolClone["Name"]);
            Assert.Equal(school.Id, schoolClone["Id"]);

            schoolValues["Name"] = "Further Modified";
            Assert.Equal("Test School", schoolClone["Name"]);

            var departmentsProperty = schoolValues.ComplexCollectionProperties.Single(p => p.Name == "Departments");
            var originalDepts = schoolValues[departmentsProperty];
            var clonedDepts = schoolClone[departmentsProperty];
            Assert.NotSame(originalDepts, clonedDepts);
        }
    }

    [ConditionalFact(Skip = "Complex collection query support. Issue #31411")]
    public virtual Task Store_values_can_be_cloned()
        => Store_values_can_be_cloned_implementation(e => Task.FromResult(e.GetDatabaseValues()!));

    [ConditionalFact(Skip = "Complex collection query support. Issue #31411")]
    public virtual Task Store_values_can_be_cloned_asynchronously()
        => Store_values_can_be_cloned_implementation(e => e.GetDatabaseValuesAsync()!);

    private async Task Store_values_can_be_cloned_implementation(
        Func<EntityEntry, Task<PropertyValues>> getPropertyValues)
    {
        using var context = CreateContext();
        var building = context.Set<Building>().Single(b => b.Name == "Building One");

        building.Name = "Building One Prime";
        building.Value = 1500001m;
        context.Entry(building).Property("Shadow1").CurrentValue = 12;
        context.Entry(building).Property("Shadow2").CurrentValue = "Pine Walk";

        var values = await getPropertyValues(context.Entry(building));
        var clone = values.Clone();

        Assert.NotSame(values, clone);
        Assert.Equal("Building One", clone["Name"]);
        Assert.Equal(1500000m, clone["Value"]);
        Assert.Equal(11, clone["Shadow1"]);
        Assert.Equal("Meadow Drive", clone["Shadow2"]);

        values["Name"] = "Modified";
        Assert.Equal("Building One", clone["Name"]);

        if (context.Model.FindEntityType(typeof(School)) != null)
        {
            //var school = context.Set<School>().First();

            // Complex collection query support. Issue #31411
            var school = CreateSchool();
            context.Set<School>().Attach(school);
            school.Name = "Modified School";
            school.Departments[0].Name = "Modified Department";
            school.Departments[0].Courses[0].Name = "Modified Course";
            school.Departments[0].Courses[0].Credits = 999;

            var schoolValues = await getPropertyValues(context.Entry(school));
            var schoolClone = schoolValues.Clone();

            Assert.NotSame(schoolValues, schoolClone);
            Assert.Equal("Test School", schoolClone["Name"]);
            Assert.Equal(school.Id, schoolClone["Id"]);

            schoolValues["Name"] = "Further Modified";
            Assert.Equal("Test School", schoolClone["Name"]);

            var departmentsProperty = schoolValues.Properties.Single(p => p.Name == "Departments");
            var originalDepts = schoolValues[departmentsProperty];
            var clonedDepts = schoolClone[departmentsProperty];
            Assert.NotSame(originalDepts, clonedDepts);
        }
    }

    protected abstract class PropertyValuesBase
    {
        [NotMapped]
        public bool CreatedCalled { get; set; }

        [NotMapped]
        public bool InitializingCalled { get; set; }

        [NotMapped]
        public bool InitializedCalled { get; set; }
    }

    protected abstract class Employee : UnMappedPersonBase
    {
        public int EmployeeId { get; set; }
    }

    protected class VirtualTeam : PropertyValuesBase
    {
        public int Id { get; set; }
        public string? TeamName { get; set; }
        public ICollection<CurrentEmployee>? Employees { get; set; }
    }

    protected class Building : PropertyValuesBase
    {
        private Building()
        {
        }

        public static Building Create(Guid buildingId, string name, decimal value, int? tag = null)
            => new()
            {
                BuildingId = buildingId,
                Name = name + tag,
                Value = value + (tag ?? 0),
                Culture = new Culture
                {
                    License = new License
                    {
                        Charge = 1.0m + (tag ?? 0),
                        Tag = new Tag { Text = "Ta1" + tag },
                        Title = "Ti1" + tag,
                        Tog = new Tog { Text = "To1" + tag }
                    },
                    Manufacturer = new Manufacturer
                    {
                        Name = "M1" + tag,
                        Rating = 7 + (tag ?? 0),
                        Tag = new Tag { Text = "Ta2" + tag },
                        Tog = new Tog { Text = "To2" + tag }
                    },
                    Rating = 8 + (tag ?? 0),
                    Species = "S1" + tag,
                    Validation = false
                },
                Milk = new Milk
                {
                    License = new License
                    {
                        Charge = 1.0m + (tag ?? 0),
                        Tag = new Tag { Text = "Ta1" + tag },
                        Title = "Ti1" + tag,
                        Tog = new Tog { Text = "To1" + tag }
                    },
                    Manufacturer = new Manufacturer
                    {
                        Name = "M1" + tag,
                        Rating = 7 + (tag ?? 0),
                        Tag = new Tag { Text = "Ta2" + tag },
                        Tog = new Tog { Text = "To2" + tag }
                    },
                    Rating = 8 + (tag ?? 0),
                    Species = "S1" + tag,
                    Validation = false
                }
            };

        public Guid BuildingId { get; set; }
        public string? Name { get; set; }
        public decimal Value { get; set; }
        public virtual ICollection<Office> Offices { get; } = [];
        public virtual IList<MailRoom> MailRooms { get; } = [];

        public int? PrincipalMailRoomId { get; set; }
        public MailRoom? PrincipalMailRoom { get; set; }

        public string? NotInModel { get; set; }

        private string _noGetter = "NoGetter";

        public string NoGetter
        {
            set => _noGetter = value;
        }

        public string GetNoGetterValue()
            => _noGetter;

        public string NoSetter
            => "NoSetter";

        public Culture Culture { get; set; }
        public required Milk Milk { get; set; }
    }

    [ComplexType]
    public class Address33307
    {
        public required string Street { get; set; }
        public double? Altitude { get; set; }
        public int? Number { get; set; }
    }

    public abstract class Contact33307
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public required Address33307 Address { get; set; }
    }

    public class Supplier33307 : Contact33307
    {
        public string? Foo { get; set; }
    }

    public class Customer33307 : Contact33307
    {
        public int Bar { get; set; }
    }

    protected struct Culture
    {
        public string Species { get; set; }
        public string? Subspecies { get; set; }
        public int Rating { get; set; }
        public bool? Validation { get; set; }
        public Manufacturer Manufacturer { get; set; }
        public License License { get; set; }
    }

    protected class Milk
    {
        public string Species { get; set; } = null!;
        public string? Subspecies { get; set; }
        public int Rating { get; set; }
        public bool? Validation { get; set; }
        public Manufacturer Manufacturer { get; set; } = null!;
        public License License { get; set; }
    }

    protected class Manufacturer
    {
        public string? Name { get; set; }
        public int Rating { get; set; }
        public Tag Tag { get; set; } = null!;
        public Tog Tog { get; set; }
    }

    protected struct License
    {
        public string Title { get; set; }
        public decimal Charge { get; set; }
        public Tag Tag { get; set; }
        public Tog Tog { get; set; }
    }

    protected class Tag
    {
        public string? Text { get; set; }
    }

    protected struct Tog
    {
        public string? Text { get; set; }
    }

    protected class BuildingDto
    {
        public Guid BuildingId { get; set; }
        public string? Name { get; set; }
        public decimal Value { get; set; }

        public int? PrincipalMailRoomId { get; set; }

        public string? NotInModel { get; set; }

        private string _noGetter = "NoGetter";

        public string NoGetter
        {
            set => _noGetter = value;
        }

        public string GetNoGetterValue()
            => _noGetter;

        public string NoSetter
            => "NoSetter";

        public int Shadow1 { get; set; }
    }

    protected class BuildingDtoNoKey
    {
        public string? Name { get; set; }
        public decimal Value { get; set; }
        public string? Shadow2 { get; set; }
    }

    protected class MailRoom : PropertyValuesBase
    {
#pragma warning disable IDE1006 // Naming Styles
        public int id { get; set; }
#pragma warning restore IDE1006 // Naming Styles
        public Building? Building { get; set; }
        public Guid BuildingId { get; set; }
    }

    protected class Office : UnMappedOfficeBase
    {
        public Guid BuildingId { get; set; }
        public Building? Building { get; set; }
        public IList<Whiteboard> WhiteBoards { get; } = [];
    }

    protected abstract class UnMappedOfficeBase : PropertyValuesBase
    {
        public string? Number { get; set; }
        public string? Description { get; set; }
    }

    protected class BuildingDetail : PropertyValuesBase
    {
        public Guid BuildingId { get; set; }
        public Building? Building { get; set; }
        public string? Details { get; set; }
    }

    protected class WorkOrder : PropertyValuesBase
    {
        public int WorkOrderId { get; set; }
        public int EmployeeId { get; set; }
        public Employee? Employee { get; set; }
        public string? Details { get; set; }
    }

    protected class Whiteboard : PropertyValuesBase
    {
#pragma warning disable IDE1006 // Naming Styles
        public byte[]? iD { get; set; }
#pragma warning restore IDE1006 // Naming Styles
        public string? AssetTag { get; set; }
        public Office? Office { get; set; }
    }

    protected class UnMappedPersonBase : PropertyValuesBase
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }

    protected class UnMappedOffice : Office;

    protected class CurrentEmployee : Employee
    {
        public CurrentEmployee? Manager { get; set; }
        public decimal LeaveBalance { get; set; }
        public Office? Office { get; set; }
        public ICollection<VirtualTeam>? VirtualTeams { get; set; }
    }

    protected class PastEmployee : Employee
    {
        public DateTime TerminationDate { get; set; }
    }
    protected class SchoolDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public List<DepartmentDto?> Departments { get; set; } = [];
    }

    protected class DepartmentDto
    {
        public string Name { get; set; } = null!;
        public string Building { get; set; } = null!;
        public List<CourseDto?> Courses { get; set; } = [];
    }

    protected class CourseDto
    {
        public string Name { get; set; } = null!;
        public int Credits { get; set; }
    }

    protected class School : PropertyValuesBase
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public List<Department> Departments { get; set; } = [];
    }

    [ComplexType]
    protected class Department
    {
        public string Name { get; set; } = null!;
        public string Building { get; set; } = null!;
        public List<Course> Courses { get; set; } = [];
    }

    [ComplexType]
    protected class Course
    {
        public string Name { get; set; } = null!;
        public int Credits { get; set; }
    }

    protected DbContext CreateContext()
    {
        var context = Fixture.CreateContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        return context;
    }

    private static School CreateSchool() => new School
    {
        Id = 1,
        Name = "Test School",
        Departments =
            [
                new Department
                {
                    Name = "Computer Science",
                    Building = "Building A",
                    Courses =
                    [
                        new Course { Name = "Data Structures", Credits = 3 },
                        new Course { Name = "Algorithms", Credits = 4 }
                    ]
                },
                new Department
                {
                    Name = "Mathematics",
                    Building = "Building B",
                    Courses =
                    [
                        new Course { Name = "Calculus I", Credits = 4 },
                        new Course { Name = "Linear Algebra", Credits = 3 }
                    ]
                }
            ]
    };

    public abstract class PropertyValuesFixtureBase : SharedStoreFixtureBase<PoolableDbContext>
    {
        protected override string StoreName
            => "PropertyValues";

        protected override IServiceCollection AddServices(IServiceCollection serviceCollection)
            => base.AddServices(serviceCollection.AddSingleton<ISingletonInterceptor, PropertyValuesMaterializationInterceptor>());

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            modelBuilder.Entity<Employee>(
                b =>
                {
                    b.Property(e => e.EmployeeId).ValueGeneratedNever();
                    b.Property<int>("Shadow1");
                    b.Property<string>("Shadow2");
                });

            modelBuilder.Entity<CurrentEmployee>(
                b =>
                {
                    b.Property<int>("Shadow3");

                    b.HasMany(p => p.VirtualTeams)
                        .WithMany(p => p.Employees)
                        .UsingEntity<Dictionary<string, object>>(
                            "VirtualTeamEmployee",
                            j => j
                                .HasOne<VirtualTeam>()
                                .WithMany(),
                            j => j
                                .HasOne<CurrentEmployee>()
                                .WithMany(),
                            j => j.IndexerProperty<string>("Payload"));
                });

            modelBuilder.Entity<PastEmployee>(b => b.Property<string>("Shadow4"));

            modelBuilder.Entity<Building>()
                .HasOne<MailRoom>(nameof(Building.PrincipalMailRoom))
                .WithMany()
                .HasForeignKey(b => b.PrincipalMailRoomId);

            modelBuilder.Entity<MailRoom>()
                .HasOne<Building>(nameof(MailRoom.Building))
                .WithMany(nameof(Building.MailRooms))
                .HasForeignKey(m => m.BuildingId);

            modelBuilder.Entity<Office>().HasKey(
                o => new { o.Number, o.BuildingId });

            modelBuilder.Ignore<UnMappedOffice>();

            modelBuilder.Entity<BuildingDetail>(
                b =>
                {
                    b.HasKey(d => d.BuildingId);
                    b.HasOne(d => d.Building).WithOne().HasPrincipalKey<Building>(e => e.BuildingId);
                });

            modelBuilder.Entity<Building>(
                b =>
                {
                    b.Ignore(e => e.NotInModel);
                    b.Property<int>("Shadow1");
                    b.Property<string>("Shadow2");

                    b.ComplexProperty(e => e.Culture);
                    b.ComplexProperty(e => e.Milk);
                });

            modelBuilder.Entity<Contact33307>();
            modelBuilder.Entity<Supplier33307>();
            modelBuilder.Entity<Customer33307>();

            modelBuilder.Entity<School>(
                b => b.ComplexCollection(e => e.Departments));
        }

        protected override Task SeedAsync(PoolableDbContext context)
        {
            var buildings = new List<Building>
            {
                Building.Create(new Guid("21EC2020-3AEA-1069-A2DD-08002B30309D"), "Building One", 1500000),
                Building.Create(Guid.NewGuid(), "Building Two", 1000000m)
            };

            foreach (var building in buildings)
            {
                context.Add(building);
            }

            context.Entry(buildings[0]).Property("Shadow1").CurrentValue = 11;
            context.Entry(buildings[0]).Property("Shadow2").CurrentValue = "Meadow Drive";

            context.Entry(buildings[1]).Property("Shadow1").CurrentValue = 807;
            context.Entry(buildings[1]).Property("Shadow2").CurrentValue = "Onyx Circle";

            var offices = new List<Office>
            {
                new() { BuildingId = buildings[0].BuildingId, Number = "1/1221" },
                new() { BuildingId = buildings[0].BuildingId, Number = "1/1223" },
                new() { BuildingId = buildings[0].BuildingId, Number = "2/1458" },
                new() { BuildingId = buildings[0].BuildingId, Number = "2/1789" }
            };

            foreach (var office in offices)
            {
                context.Add(office);
            }

            var teams = new List<VirtualTeam>
            {
                new() { TeamName = "Build" },
                new() { TeamName = "Test" },
                new() { TeamName = "DevOps" }
            };

            var employees = new List<Employee>
            {
                new CurrentEmployee
                {
                    EmployeeId = 1,
                    FirstName = "Rowan",
                    LastName = "Miller",
                    LeaveBalance = 45,
                    Office = offices[0],
                    VirtualTeams = [teams[0], teams[1]]
                },
                new CurrentEmployee
                {
                    EmployeeId = 2,
                    FirstName = "Arthur",
                    LastName = "Vickers",
                    LeaveBalance = 62,
                    Office = offices[1],
                    VirtualTeams = [teams[1], teams[2]]
                },
                new PastEmployee
                {
                    EmployeeId = 3,
                    FirstName = "John",
                    LastName = "Doe",
                    TerminationDate = new DateTime(2006, 1, 23)
                }
            };

            context.Entry(employees[0]).Property("Shadow1").CurrentValue = 111;
            context.Entry(employees[0]).Property("Shadow2").CurrentValue = "PM";
            context.Entry(employees[0]).Property("Shadow3").CurrentValue = 1111;

            context.Entry(employees[1]).Property("Shadow1").CurrentValue = 222;
            context.Entry(employees[1]).Property("Shadow2").CurrentValue = "SDE";
            context.Entry(employees[1]).Property("Shadow3").CurrentValue = 11112;

            context.Entry(employees[2]).Property("Shadow1").CurrentValue = 333;
            context.Entry(employees[2]).Property("Shadow2").CurrentValue = "SDET";
            context.Entry(employees[2]).Property("Shadow4").CurrentValue = "BSC";

            foreach (var employee in employees)
            {
                context.Add(employee);
            }

            var whiteboards = new List<Whiteboard>
            {
                new()
                {
                    AssetTag = "WB1973",
                    iD = [1, 9, 7, 3],
                    Office = offices[0]
                },
                new()
                {
                    AssetTag = "WB1977",
                    iD = [1, 9, 7, 7],
                    Office = offices[0]
                },
                new()
                {
                    AssetTag = "WB1970",
                    iD = [1, 9, 7, 0],
                    Office = offices[2]
                }
            };

            foreach (var whiteboard in whiteboards)
            {
                context.Add(whiteboard);
            }

            foreach (var joinEntry in context.ChangeTracker.Entries<Dictionary<string, object>>())
            {
                joinEntry.Property("Payload").CurrentValue = "Payload";

                Assert.True((bool)joinEntry.Entity["CreatedCalled"]);
                Assert.True((bool)joinEntry.Entity["InitializingCalled"]);
                Assert.True((bool)joinEntry.Entity["InitializedCalled"]);
            }

            context.Add(
                new Supplier33307
                {
                    Name = "Foo",
                    Address = new Address33307
                    {
                        Street = "One",
                        Altitude = Math.PI,
                        Number = 42,
                    },
                    Foo = "F"
                }); context.Add(
                new Customer33307
                {
                    Name = "Bar",
                    Address = new Address33307
                    {
                        Street = "Two",
                        Altitude = Math.E,
                        Number = 42,
                    },
                    Bar = 11
                });

            // Complex collection query support. Issue #31411
            //context.Add(CreateSchool());

            return context.SaveChangesAsync();
        }
    }

    public class PropertyValuesMaterializationInterceptor : IMaterializationInterceptor
    {
        public InterceptionResult<object> CreatingInstance(
            MaterializationInterceptionData materializationData,
            InterceptionResult<object> result)
            => result;

        public object CreatedInstance(MaterializationInterceptionData materializationData, object entity)
        {
            if (entity is IDictionary<string, object> joinEntity)
            {
                joinEntity["CreatedCalled"] = true;
            }
            else if (entity is PropertyValuesBase propertyValuesBase)
            {
                propertyValuesBase.CreatedCalled = true;
            }

            return entity;
        }

        public InterceptionResult InitializingInstance(
            MaterializationInterceptionData materializationData,
            object entity,
            InterceptionResult result)
        {
            if (entity is IDictionary<string, object> joinEntity)
            {
                joinEntity["InitializingCalled"] = true;
            }
            else if (entity is PropertyValuesBase propertyValuesBase)
            {
                propertyValuesBase.InitializingCalled = true;
            }

            return result;
        }

        public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
        {
            if (entity is IDictionary<string, object> joinEntity)
            {
                joinEntity["InitializedCalled"] = true;
            }
            else if (entity is PropertyValuesBase propertyValuesBase)
            {
                propertyValuesBase.InitializedCalled = true;
            }

            return entity;
        }
    }
}
