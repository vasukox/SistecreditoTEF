using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

public partial class ConfigurarAdminPage : ContentPage
{
    public ConfigurarAdminPage(ConfigurarAdminViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
