using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using SistecreditoTEF.Maui.Common;
using SistecreditoTEF.Maui.Services.Hiopos;
using SistecreditoTEF.Maui.Services.Platform;

namespace SistecreditoTEF.Maui;

/// <summary>
/// MainActivity del APK Abonos. Solo entry point standalone (LAUNCHER).
///
/// NO procesa intents de HI-POS. Si HI-POS necesita el modulo TEF,
/// dispara el otro APK (com.permoda.sistecreditotef), no este.
///
/// Comportamiento:
///   - Al abrir (LAUNCHER): navega directo a CreditosActivosPage
///     para que el cajero pueda hacer abonos sin pasar por HomePage.
///
/// Como es un APK independiente, no comparte proceso con el TEF:
/// la colisión de modos que teniamos antes YA NO ES POSIBLE.
/// </summary>
[Activity(
    Label = "Sistecredito Abonos",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize
                          | ConfigChanges.KeyboardHidden | ConfigChanges.Locale
                          | ConfigChanges.LayoutDirection,
    Exported = true)]
[IntentFilter(
    actions: new[] { "android.intent.action.MAIN" },
    Categories = new[] { "android.intent.category.LAUNCHER" })]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Log.Info("AbonosMainActivity", "APK Abonos arrancado");
    }

    protected override void OnResume()
    {
        base.OnResume();

        if (IPlatformApplication.Current?.Services is null) return;

        // Navegamos directo a la pantalla de abonos para que el cajero
        // no tenga que tocar nada mas. Skip si ya esta ahi.
        var standalone = IPlatformApplication.Current.Services
            .GetService(typeof(IStandaloneModeTracker)) as IStandaloneModeTracker;
        if (standalone is not null) standalone.IsStandalone = true;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Shell.Current is null) return;
            var currentRoute = Shell.Current.CurrentItem?.Route ?? string.Empty;
            if (currentRoute != "creditosActivos")
                await Shell.Current.GoToAsync(AppRoutes.CreditosActivos);
        });
    }
}