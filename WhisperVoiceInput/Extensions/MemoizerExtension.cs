using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace WhisperVoiceInput.Extensions;

/// <summary>
/// https://stackoverflow.com/a/53299290
/// </summary>
public static class MemoizerExtension
{
    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<string, object>> WeakCache = new();

    public static TResult Memoized<T1, TResult>(
        this object context,
        T1 arg,
        Func<T1, TResult> f,
        [CallerMemberName] string? cacheKey = null)
        where T1 : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cacheKey);

        var objCache = WeakCache.GetOrCreateValue(context);

        var methodCache = (ConcurrentDictionary<T1, TResult>) objCache
            .GetOrAdd(cacheKey, _ => new ConcurrentDictionary<T1, TResult>());

        return methodCache.GetOrAdd(arg, f);
    }
}