using System.ComponentModel;
using Avalonia.Controls;
using PaperNexus.ViewModels;

namespace PaperNexus.Views;

// The Secrets page. MainView opens it over the tabs as a modal page while the Settings view
// model says Secrets is open, and closes it when that ends; a recreated screen opens it again.
// Android's back closes it, as Close does.
public partial class SecretsPage : ContentPage
{
    private SecretsViewModel? _secrets;

    public SecretsPage()
    {
        InitializeComponent();
        ProgressTrack.SizeChanged += (_, _) => SizeBar();
    }

    protected override bool OnSystemBackButtonPressed()
    {
        if (DataContext is SettingsViewModel settings)
            settings.CloseSecretsCommand.Execute(null);
        return true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_secrets is not null)
            _secrets.PropertyChanged -= OnSecretsPropertyChanged;
        _secrets = (DataContext as SettingsViewModel)?.Secrets;
        if (_secrets is not null)
            _secrets.PropertyChanged += OnSecretsPropertyChanged;
        SizeBar();
    }

    private void OnSecretsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SecretsViewModel.FoundFraction))
            SizeBar();
    }

    // The bar is the found share of the track's inside width, as on the desktop.
    private void SizeBar()
    {
        var fraction = _secrets?.FoundFraction ?? 0;
        var inside = ProgressTrack.Bounds.Width - ProgressTrack.BorderThickness.Left - ProgressTrack.BorderThickness.Right;
        ProgressBar.Width = Math.Max(0, inside * fraction);
    }
}
