using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace WhisperVoiceInput.Extensions
{
    // Extension method for pausing observables
    public static class ObservableExtensions
    {
        public static IObservable<T?> PausableLatest<T>(
            this IObservable<T?> source,
            IObservable<bool> pauser)
        {
            return Observable.Create<T?>(observer =>
            {
                // Lock object for thread safety.
                object sync = new object();
                // The latest value and whether we have one.
                var latest = default(T);
                bool hasLatest = false;
                // Keep track of the current gate state.
                bool isOpen = true; // Default to open

                // Subscribe to the pauser (gate) observable.
                var pauserSubscription = pauser
                    .DistinctUntilChanged()
                    .Subscribe(
                        state =>
                        {
                            lock (sync)
                            {
                                isOpen = state;
                                // When gate opens, emit the latest buffered value if available.
                                if (isOpen && hasLatest)
                                {
                                    observer.OnNext(latest);
                                    hasLatest = false;
                                }
                            }
                        },
                        observer.OnError);

                // Subscribe to the source observable.
                var sourceSubscription = source.Subscribe(
                    item =>
                    {
                        lock (sync)
                        {
                            if (isOpen)
                            {
                                // If gate is open, emit immediately.
                                observer.OnNext(item);
                            }
                            else
                            {
                                // If gate is closed, store the latest value.
                                latest = item;
                                hasLatest = true;
                            }
                        }
                    },
                    ex =>
                    {
                        lock (sync)
                        {
                            observer.OnError(ex);
                        }
                    },
                    () =>
                    {
                        lock (sync)
                        {
                            // On completion, if there's a buffered value, emit it before completing.
                            if (hasLatest)
                            {
                                observer.OnNext(latest);
                                hasLatest = false;
                            }
                            observer.OnCompleted();
                        }
                    });

                return new CompositeDisposable(pauserSubscription, sourceSubscription);
            });
        }
    }
}