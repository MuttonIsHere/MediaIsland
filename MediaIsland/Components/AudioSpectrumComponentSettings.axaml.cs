using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using MediaIsland.Services.Audio.Visualization;

namespace MediaIsland.Components;

/// <summary>
/// 音频频谱的设置页。只对当前样式生效的行按 <see cref="AudioSpectrumConfig.ApplicableSettings"/>
/// 显隐——设置页上每一项都要对眼前的画面有作用。
/// </summary>
public partial class AudioSpectrumComponentSettings : ComponentBase<AudioSpectrumConfig>
{
    public AudioSpectrumComponentSettings(IAudioVisualSourceInfo sourceInfo, AudioVisualizationService visualization)
    {
        InitializeComponent();
        Header.Attach(sourceInfo, visualization);
        AttachedToVisualTree += (_, _) =>
        {
            Settings.PropertyChanged += OnSettingsChanged;
            ApplyVisibility();
        };
        DetachedFromVisualTree += (_, _) => Settings.PropertyChanged -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioSpectrumConfig.Style)) ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        var applicable = AudioSpectrumConfig.ApplicableSettings(Settings.Style);
        SetRowVisible("BandCountRow", applicable.Contains(AudioVisualSetting.BandCount));
        SetRowVisible("BarGapRow", applicable.Contains(AudioVisualSetting.BarGap));
        SetRowVisible("MirrorRow", applicable.Contains(AudioVisualSetting.Mirror));
    }

    // 这里按名字查找而不是直接用 x:Name 生成字段：字段生成本身依赖 XAML 编译器版本，
    // 而设置页要同时兼容宿主自带的老 Avalonia。名字本身仍是 axaml 里的 x:Name。
    private void SetRowVisible(string name, bool isVisible)
    {
        if (this.FindControl<Control>(name) is { } row)
        {
            row.IsVisible = isVisible;
        }
    }
}
