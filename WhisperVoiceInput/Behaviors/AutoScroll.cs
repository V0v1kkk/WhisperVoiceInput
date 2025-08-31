using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace WhisperVoiceInput.Behaviors;

public static class AutoScroll
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "IsEnabled",
            typeof(AutoScroll));

    static AutoScroll()
    {
        IsEnabledProperty.Changed.Subscribe(args =>
        {
            if (args.Sender is ItemsControl list)
            {
                if (args.NewValue.HasValue && args.NewValue.Value) Attach(list);
                // detaching is implicit; we still keep handlers but guard by IsEnabled at runtime
            }
        });
    }

    public static void SetIsEnabled(AvaloniaObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);

    private static void Attach(ItemsControl list)
    {
        ScrollViewer? scrollViewer = null;
        bool pending = false;
        System.Collections.Specialized.INotifyCollectionChanged? incc = null;

        void EnsureScrollViewer()
        {
            if (scrollViewer == null)
            {
                scrollViewer = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                // no-op: we don't depend on ScrollChanged anymore
                _ = scrollViewer;
            }
        }

        void Schedule()
        {
            if (pending) return;
            pending = true;
            Dispatcher.UIThread.Post(() =>
            {
                pending = false;
                if (!GetIsEnabled(list)) return;
                EnsureScrollViewer();
                if (scrollViewer == null) return;
                if (list.ItemCount <= 0) return;
                if (!list.IsAttachedToVisualTree()) return;
                if (double.IsNaN(scrollViewer.Bounds.Width) || double.IsNaN(scrollViewer.Bounds.Height)) return;
                if (scrollViewer.Bounds.Width <= 0 || scrollViewer.Bounds.Height <= 0) return;
                try { scrollViewer.ScrollToEnd(); } catch { /* ignore */ }
            }, DispatcherPriority.Background);
        }

        list.AttachedToVisualTree += (_, __) => { EnsureScrollViewer(); Schedule(); };

        // React to ItemsSource changes and hook collection changed
        void HookItems(object? src)
        {
            if (incc != null)
            {
                incc.CollectionChanged -= OnCollectionChanged;
                incc = null;
            }
            if (src is System.Collections.Specialized.INotifyCollectionChanged c)
            {
                incc = c;
                incc.CollectionChanged += OnCollectionChanged;
            }
        }

        void OnCollectionChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (!GetIsEnabled(list)) return;
            if (e.Action 
                is System.Collections.Specialized.NotifyCollectionChangedAction.Add 
                or System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                Schedule();
            }
        }

        // Initial hook
        HookItems(list.ItemsSource);
        // Rehook on ItemsSource changes
        list.GetObservable(ItemsControl.ItemsSourceProperty).Subscribe(HookItems);

        // Initial scroll on attach if enabled
        Schedule();
    }
}


