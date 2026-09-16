using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels;

public partial class NativeSettingViewModel : ObservableObject
{
    private readonly string _original;
    public NativeSettingViewModel(NativeSettingDefinition definition, string value)
    {
        Definition = definition;
        _original = value;
        UseDefault = value == "default";
        Value = UseDefault ? definition.Kind switch
        {
            "Number" => "1", "Toggle" => "true", "Choice" => definition.Choices[0], _ => string.Empty
        } : value;
    }

    public NativeSettingDefinition Definition { get; }
    [ObservableProperty] public partial bool UseDefault { get; set; }
    [ObservableProperty] public partial string Value { get; set; } = string.Empty;
    public string Label => LocalizationService.GetString("NativeField" + Definition.Key, Definition.Key);
    public string Hint => LocalizationService.GetString("NativeHint" + Definition.Key, Definition.Path);
    public bool IsNumber => Definition.Kind == "Number";
    public bool IsText => Definition.Kind == "Text";
    public bool IsChoice => Definition.Kind == "Choice";
    public bool IsToggle => Definition.Kind == "Toggle";
    public bool IsCustom => !UseDefault;
    public string EffectiveValue => UseDefault ? "default" : Value;
    public bool IsDirty => EffectiveValue != _original;
    // Leave unfamiliar existing values intact until that specific field is edited.
    public bool IsValid => !IsDirty || Definition.IsValid(EffectiveValue);
    public string Validation => UseDefault || Definition.IsValid(Value) ? string.Empty :
        LocalizationService.GetString(IsDirty ? "NativeFieldInvalid" : "NativeFieldUnrecognized", "Invalid value");
    public double? NumericValue
    {
        get => double.TryParse(Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
        set
        {
            if (!Nullable.Equals(value, NumericValue))
                Value = value is null || double.IsNaN(value.Value) ? string.Empty : value.Value.ToString(CultureInfo.InvariantCulture);
        }
    }
    public string? SelectedChoice { get => Value; set { if (value is not null) Value = value; } }
    public bool ToggleValue { get => Value == "true"; set { if (value != ToggleValue) Value = value ? "true" : "false"; } }
    partial void OnUseDefaultChanged(bool value) => Refresh();
    partial void OnValueChanged(string value) => Refresh();
    public void Refresh()
    {
        foreach (var name in new[] { nameof(Label), nameof(Hint), nameof(IsCustom), nameof(IsDirty), nameof(IsValid), nameof(Validation), nameof(NumericValue), nameof(ToggleValue), nameof(SelectedChoice) })
            OnPropertyChanged(name);
    }
}
