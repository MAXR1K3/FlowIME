using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace FlowIME.App.Layout;

/// <summary>Use the space owned by the content, not the width hidden behind navigation.
/// Text scaling promotes the compact layout before labels are squeezed.</summary>
public sealed class ContentWidthTrigger : StateTriggerBase
{
    private readonly UISettings _settings = new();
    private bool _listening;

    public static readonly DependencyProperty TargetElementProperty = DependencyProperty.Register(
        nameof(TargetElement), typeof(FrameworkElement), typeof(ContentWidthTrigger),
        new PropertyMetadata(null, OnTargetChanged));
    public static readonly DependencyProperty MinWidthProperty = DependencyProperty.Register(
        nameof(MinWidth), typeof(double), typeof(ContentWidthTrigger),
        new PropertyMetadata(0d, OnThresholdChanged));

    public static readonly DependencyProperty MaxWidthProperty = DependencyProperty.Register(
        nameof(MaxWidth), typeof(double), typeof(ContentWidthTrigger),
        new PropertyMetadata(double.PositiveInfinity, OnThresholdChanged));
    public double MaxWidth
    {
        get => (double)GetValue(MaxWidthProperty);
        set => SetValue(MaxWidthProperty, value);
    }

    public FrameworkElement? TargetElement
    {
        get => (FrameworkElement?)GetValue(TargetElementProperty);
        set => SetValue(TargetElementProperty, value);
    }
    public double MinWidth
    {
        get => (double)GetValue(MinWidthProperty);
        set => SetValue(MinWidthProperty, value);
    }

    private static void OnThresholdChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ContentWidthTrigger)sender).Update();

    private static void OnTargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var trigger = (ContentWidthTrigger)sender;
        trigger.StopListening();
        if (args.OldValue is FrameworkElement old)
        {
            old.SizeChanged -= trigger.OnSizeChanged;
            old.Loaded -= trigger.OnLoaded;
            old.Unloaded -= trigger.OnUnloaded;
        }
        if (args.NewValue is FrameworkElement target)
        {
            target.SizeChanged += trigger.OnSizeChanged;
            target.Loaded += trigger.OnLoaded;
            target.Unloaded += trigger.OnUnloaded;
            if (target.IsLoaded) trigger.StartListening();
        }
        trigger.Update();
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => Update();
    private void OnLoaded(object sender, RoutedEventArgs args) { StartListening(); Update(); }
    private void OnUnloaded(object sender, RoutedEventArgs args) => StopListening();
    private void StartListening()
    {
        if (_listening) return;
        _settings.TextScaleFactorChanged += OnTextScaleChanged;
        _listening = true;
    }
    private void StopListening()
    {
        if (!_listening) return;
        _settings.TextScaleFactorChanged -= OnTextScaleChanged;
        _listening = false;
    }
    private void OnTextScaleChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(Update);
    private void Update() => SetActive(TargetElement is { } target &&
        target.ActualWidth >= MinWidth * Math.Max(1d, _settings.TextScaleFactor) &&
        target.ActualWidth < MaxWidth * Math.Max(1d, _settings.TextScaleFactor));
}
