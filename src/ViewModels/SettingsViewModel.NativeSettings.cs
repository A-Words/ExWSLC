using System.Collections.ObjectModel;
using ExWSLC.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels;

public partial class SettingsViewModel
{
    private NativeSettingsDocument? _nativeSettingsOriginal;
    private string _nativeConfigStatusKey = "NativeConfigNotLoaded";
    public ObservableCollection<NativeSettingViewModel> NativeSettingsFields { get; } = [];
    private bool HasNativeSettingsChanges => NativeSettingsFields.Any(setting => setting.IsDirty);
    [ObservableProperty] public partial bool IsNativeSettingsBusy { get; set; }
    public bool NativeSettingsLoaded => _nativeSettingsOriginal is not null;
    public bool CanEditNativeSettings => NativeSettingsLoaded && !IsNativeSettingsBusy && !Workspace.IsBusy;
    public bool CanLoadNativeSettings => !IsNativeSettingsBusy && !Workspace.IsBusy;
    public bool CanSaveNativeSettings => CanEditNativeSettings && HasNativeSettingsChanges && NativeSettingsFields.All(setting => setting.IsValid);
    public string NativeConfigStatus => LocalizationService.GetString(_nativeConfigStatusKey, _nativeConfigStatusKey);

    partial void OnIsNativeSettingsBusyChanged(bool value) => RaiseNativeSettingsChanged();

    private void RaiseNativeSettingsChanged()
    {
        OnPropertyChanged(nameof(NativeSettingsLoaded));
        OnPropertyChanged(nameof(CanEditNativeSettings));
        OnPropertyChanged(nameof(CanLoadNativeSettings));
        OnPropertyChanged(nameof(CanSaveNativeSettings));
        OnPropertyChanged(nameof(NativeConfigStatus));
        LoadNativeSettingsCommand.NotifyCanExecuteChanged();
        SaveNativeSettingsCommand.NotifyCanExecuteChanged();
        ResetNativeSettingsCommand.NotifyCanExecuteChanged();
    }

    private void SetNativeSettingsDocument(NativeSettingsDocument document)
    {
        var fields = NativeSettingDefinition.All.Select(definition => new NativeSettingViewModel(
            definition, NativeSettingsYaml.Read(document.Text, definition.Path))).ToArray();
        NativeSettingsFields.Clear();
        foreach (var setting in fields)
        {
            setting.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(NativeSettingViewModel.Value) or nameof(NativeSettingViewModel.UseDefault))
                    RaiseNativeSettingsChanged();
            };
            NativeSettingsFields.Add(setting);
        }
        _nativeSettingsOriginal = document;
    }

    [RelayCommand(CanExecute = nameof(CanLoadNativeSettings))]
    private async Task LoadNativeSettingsAsync()
    {
        IsNativeSettingsBusy = true;
        try
        {
            if (_nativeSettingsOriginal is not null && HasNativeSettingsChanges &&
                !await Workspace.Interaction.ConfirmAsync(
                    LocalizationService.GetString("NativeConfigReload", "Reload"),
                    LocalizationService.GetString("NativeConfigDiscard", "Discard unsaved changes?"))) return;
            var document = await Workspace.Runtime.ReadNativeSettingsAsync(Workspace.Lifetime.Token);
            SetNativeSettingsDocument(document);
            _nativeConfigStatusKey = document.Exists ? "NativeConfigLoaded" : "NativeConfigMissing";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { _nativeConfigStatusKey = "NativeConfigFailed"; }
        finally { IsNativeSettingsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSaveNativeSettings))]
    private async Task SaveNativeSettingsAsync()
    {
        if (!CanSaveNativeSettings) return;
        var original = _nativeSettingsOriginal!;
        IsNativeSettingsBusy = true;
        try
        {
            var text = original.Text;
            foreach (var setting in NativeSettingsFields.Where(setting => setting.IsDirty))
                text = NativeSettingsYaml.Update(text, setting.Definition.Path, setting.EffectiveValue);
            _nativeConfigStatusKey = await Workspace.Runtime.SaveNativeSettingsAsync(original, text, Workspace.Lifetime.Token);
            if (_nativeConfigStatusKey == "NativeConfigSaved")
            {
                SetNativeSettingsDocument(new(text, true));
                HostLoopbackConfiguration = null;
                HostProbeResult = null;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { _nativeConfigStatusKey = "NativeConfigFailed"; }
        finally { IsNativeSettingsBusy = false; }
    }
}
