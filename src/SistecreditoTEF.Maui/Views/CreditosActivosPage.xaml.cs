using SistecreditoTEF.Maui.ViewModels;

namespace SistecreditoTEF.Maui.Views;

public partial class CreditosActivosPage : HioposFlowPage
{
    public CreditosActivosPage(CreditosActivosViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
