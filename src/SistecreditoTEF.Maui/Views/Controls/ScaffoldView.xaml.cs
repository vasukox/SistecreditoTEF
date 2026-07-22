namespace SistecreditoTEF.Maui.Views.Controls;

/// <summary>
/// HU8-973: chrome de pantalla (header verde + contenido + footer HI-POS).
/// El botón "atrás" hace pop de la navegación (Shell "..").
/// </summary>
public partial class ScaffoldView : ContentView
{
    public static readonly BindableProperty TitleTextProperty =
        BindableProperty.Create(nameof(TitleText), typeof(string), typeof(ScaffoldView), string.Empty);
    public static readonly BindableProperty SubtitleTextProperty =
        BindableProperty.Create(nameof(SubtitleText), typeof(string), typeof(ScaffoldView), string.Empty);
    public static readonly BindableProperty EyebrowTextProperty =
        BindableProperty.Create(nameof(EyebrowText), typeof(string), typeof(ScaffoldView), string.Empty);
    public static readonly BindableProperty TrailingTextProperty =
        BindableProperty.Create(nameof(TrailingText), typeof(string), typeof(ScaffoldView), string.Empty);
    public static readonly BindableProperty ShowBackProperty =
        BindableProperty.Create(nameof(ShowBack), typeof(bool), typeof(ScaffoldView), true);

    public string TitleText    { get => (string)GetValue(TitleTextProperty);    set => SetValue(TitleTextProperty, value); }
    public string SubtitleText { get => (string)GetValue(SubtitleTextProperty); set => SetValue(SubtitleTextProperty, value); }
    public string EyebrowText  { get => (string)GetValue(EyebrowTextProperty);  set => SetValue(EyebrowTextProperty, value); }
    public string TrailingText { get => (string)GetValue(TrailingTextProperty); set => SetValue(TrailingTextProperty, value); }
    public bool   ShowBack     { get => (bool)GetValue(ShowBackProperty);       set => SetValue(ShowBackProperty, value); }

    public ScaffoldView()
    {
        InitializeComponent();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Back") is Button back)
        {
            back.Clicked -= OnBackClicked;
            back.Clicked += OnBackClicked;
        }
    }

    private static async void OnBackClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
            await Shell.Current.GoToAsync("..");
    }
}
