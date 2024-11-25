using DevToys.Api;

namespace DevToys.Geo.UnitTests.Mocks;

internal class MockISettingsProvider : ISettingsProvider
{
    private readonly Dictionary<string, object> _settings = [];

    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    public T GetSetting<T>(SettingDefinition<T> settingDefinition)
    {
        if (_settings.TryGetValue(settingDefinition.Name, out var value))
            return (T)value!;

        return settingDefinition.DefaultValue;
    }

    public void ResetSetting<T>(SettingDefinition<T> settingDefinition)
    {
        SettingChanged?.Invoke(this, new SettingChangedEventArgs(settingDefinition.Name, settingDefinition.DefaultValue));
    }

    public void SetSetting<T>(SettingDefinition<T> settingDefinition, T value)
    {
        if (value != null)
        {
            _settings[settingDefinition.Name] = value;
        }

        SettingChanged?.Invoke(this, new SettingChangedEventArgs(settingDefinition.Name, value));
    }
}
