using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PaperNexus.Core;

public static class Extensions
{
    // Guards against null arguments: throws ArgumentNullException naming the actual
    // argument expression at the call site (via CallerArgumentExpression) rather than
    // the generic local name "value", making the exception message immediately useful.
    public static T ThrowIfNull<T>(this T value, [CallerArgumentExpression("value")] string parameterName = null)
    {
        return value ?? throw new ArgumentNullException(parameterName);
    }

    // Reads every line from reader and logs it at the specified level.
    // Useful for piping process stdout/stderr into the application logger.
    public static async Task CopyTo(this StreamReader reader, ILogger logger, LogLevel level)
    {
        reader.ThrowIfNull();
        logger.ThrowIfNull();
        var line = await reader.ReadLineAsync();
        while (line is not null)
        {
            logger.Log(level, line);
            line = await reader.ReadLineAsync();
        }
    }

    // Async-friendly semaphore acquisition that returns an IDisposable whose Dispose
    // releases the semaphore, allowing usage in a `using` statement or `using var`.
    public static async Task<IDisposable> EnterAsync(this SemaphoreSlim semaphore)
    {
        semaphore.ThrowIfNull();
        await semaphore.WaitAsync();
        return new SemaphoreRelease(semaphore);
    }

    private class SemaphoreRelease : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        public void Dispose() => _semaphore.Release();
        public SemaphoreRelease(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }
    }

    public static T Max<T>(this T value1, T value2) where T : IComparable<T>
    {
        return value1.CompareTo(value2) > 0 ? value1 : value2;
    }

    // Returns an existing value for key, or creates and stores a new one using valueFactory.
    // Not thread-safe — callers must synchronise if the dictionary is shared across threads.
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> items, TKey key, Func<TKey, TValue> valueFactory)
    {
        items.ThrowIfNull();
        key.ThrowIfNull();
        if (items.TryGetValue(key, out var value))
            return value;
        valueFactory.ThrowIfNull();
        value = valueFactory(key);
        items[key] = value;
        return value;
    }
}
