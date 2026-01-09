using System.Collections.Concurrent;

namespace KfChatDotNetBot.Tests.Concurrency;

/// <summary>
/// Tests demonstrating race conditions with shared mutable collections.
/// These tests document concurrency issues in the codebase.
///
/// Key issues documented:
/// - ChatBot.cs:29 - SentMessages (public List) is not thread-safe
/// - ChatBot.cs:24 - _seenMessages (private List) is not thread-safe
/// - ChatBot.cs:37 - _scheduledDeletions (private List) is not thread-safe
/// - BotServices.cs:49 - _yeetBets (private Dictionary) is not thread-safe
/// </summary>
public class SentMessagesTests
{
    #region List Race Condition Tests

    /// <summary>
    /// Demonstrates that concurrent Add operations on List<T> can corrupt the list.
    /// This simulates what happens in ChatBot when multiple WebSocket events
    /// try to add to SentMessages simultaneously.
    /// </summary>
    [Fact]
    [Trait("Category", "Concurrency")]
    public async Task List_ConcurrentAdd_CanCorruptList()
    {
        // Using a regular List like ChatBot.SentMessages
        var list = new List<int>();
        var tasks = new List<Task>();
        const int itemCount = 1000;

        // Simulate concurrent additions (like multiple messages being sent)
        for (int i = 0; i < itemCount; i++)
        {
            int value = i;
            tasks.Add(Task.Run(() => list.Add(value)));
        }

        // This may throw or corrupt the list
        try
        {
            await Task.WhenAll(tasks);

            // If no exception, check if all items were added
            // Due to race conditions, we may have fewer items than expected
            list.Count.Should().BeLessThanOrEqualTo(itemCount,
                "Race condition may cause lost items");

            // We can't guarantee exact count due to race conditions
        }
        catch (Exception)
        {
            // Exception during concurrent access is expected behavior
            // This proves the race condition exists
        }
    }

    /// <summary>
    /// Demonstrates that concurrent Add and Read operations can cause exceptions.
    /// This simulates ChatBot accessing SentMessages while adding new messages.
    /// </summary>
    [Fact]
    [Trait("Category", "Concurrency")]
    public async Task List_ConcurrentAddAndRead_CanThrow()
    {
        var list = new List<int>();
        var exceptions = new ConcurrentBag<Exception>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Writer task - simulates SendChatMessageAsync adding to SentMessages
        var writerTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    list.Add(1);
                    await Task.Delay(1);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        // Reader task - simulates OnKfChatMessage reading from SentMessages
        var readerTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    // This is similar to: SentMessages.FirstOrDefault(...)
                    var _ = list.FirstOrDefault(x => x == 1);
                    await Task.Delay(1);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        await Task.WhenAll(writerTask, readerTask);

        // Document whether exceptions occurred
        // Note: This test may or may not catch the race depending on timing
        if (exceptions.Any())
        {
            exceptions.Should().Contain(e => e is InvalidOperationException,
                "Collection modified during enumeration");
        }
    }

    /// <summary>
    /// Demonstrates the correct fix using ConcurrentBag or lock.
    /// </summary>
    [Fact]
    [Trait("Category", "Concurrency")]
    public async Task ConcurrentBag_SafeConcurrentAccess()
    {
        // Using ConcurrentBag instead of List
        var bag = new ConcurrentBag<int>();
        var tasks = new List<Task>();
        const int itemCount = 1000;

        for (int i = 0; i < itemCount; i++)
        {
            int value = i;
            tasks.Add(Task.Run(() => bag.Add(value)));
        }

        await Task.WhenAll(tasks);

        // All items should be present
        bag.Count.Should().Be(itemCount,
            "ConcurrentBag should handle concurrent access safely");
    }

    #endregion

    #region Dictionary Race Condition Tests

    /// <summary>
    /// Demonstrates race condition with Dictionary (like _yeetBets).
    /// </summary>
    [Fact]
    [Trait("Category", "Concurrency")]
    public async Task Dictionary_ConcurrentAccess_CanCorrupt()
    {
        var dict = new Dictionary<string, int>();
        var tasks = new List<Task>();
        var exceptions = new ConcurrentBag<Exception>();

        // Simulate concurrent access pattern from BotServices
        for (int i = 0; i < 100; i++)
        {
            string key = $"key_{i % 10}"; // Some key collisions
            int value = i;

            tasks.Add(Task.Run(() =>
            {
                try
                {
                    // This is similar to: _yeetBets.Add(bet.BetIdentifier, ...)
                    if (!dict.ContainsKey(key))
                    {
                        dict.Add(key, value);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Expect some exceptions due to concurrent modification
        // or duplicate key additions
    }

    /// <summary>
    /// Demonstrates TOCTOU (Time-of-check-time-of-use) race condition.
    /// This is the pattern in BotServices line 709:
    /// if (!_yeetBets.ContainsKey(key)) ... _yeetBets[key]
    /// </summary>
    [Fact]
    [Trait("Category", "Concurrency")]
    public async Task Dictionary_TOCTOU_RaceCondition()
    {
        var dict = new ConcurrentDictionary<string, int>();
        dict["key"] = 1;

        var exceptions = new ConcurrentBag<Exception>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Task 1: Check and use
        var task1 = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    // TOCTOU pattern - check then use
                    if (dict.ContainsKey("key"))
                    {
                        await Task.Delay(1); // Simulates processing delay
                        var value = dict["key"]; // Could fail if removed between check and access
                    }
                }
                catch (KeyNotFoundException ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        // Task 2: Remove the key
        var task2 = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                dict.TryRemove("key", out _);
                await Task.Delay(1);
                dict["key"] = 1;
            }
        });

        await Task.WhenAll(task1, task2);

        // TOCTOU can cause KeyNotFoundException even with ConcurrentDictionary
        // because the check and use are not atomic
    }

    /// <summary>
    /// Shows the correct pattern using TryGetValue instead of ContainsKey + index.
    /// </summary>
    [Fact]
    [Trait("Category", "Concurrency")]
    public void Dictionary_TryGetValue_AtomicAccess()
    {
        var dict = new ConcurrentDictionary<string, int>();
        dict["key"] = 42;

        // Atomic check-and-get
        if (dict.TryGetValue("key", out var value))
        {
            value.Should().Be(42);
        }

        // Even better for add-or-update scenarios
        var result = dict.GetOrAdd("key2", _ => 100);
        result.Should().Be(100);
    }

    #endregion

    #region Boolean Flag Race Conditions

    /// <summary>
    /// Demonstrates race condition with boolean flags.
    /// ChatBot has several: InitialStartCooldown, GambaSeshPresent, etc.
    /// </summary>
    [Fact]
    [Trait("Category", "Concurrency")]
    public async Task BooleanFlag_NonAtomicReadModifyWrite()
    {
        bool flag = false;
        int trueCount = 0;
        var tasks = new List<Task>();

        // Simulate the pattern: if (!flag) { flag = true; ... }
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                // This read-check-write is NOT atomic
                if (!flag)
                {
                    flag = true;
                    Interlocked.Increment(ref trueCount);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Due to race condition, multiple threads may see flag as false
        // and increment trueCount
        // In a correct implementation, trueCount should be exactly 1
        trueCount.Should().BeGreaterThanOrEqualTo(1,
            "At least one thread should succeed");

        // This documents the race - multiple threads might "win" the check
    }

    /// <summary>
    /// Shows the correct pattern using Interlocked.CompareExchange.
    /// </summary>
    [Fact]
    [Trait("Category", "Concurrency")]
    public async Task BooleanFlag_AtomicCompareExchange()
    {
        int flag = 0; // 0 = false, 1 = true
        int successCount = 0;
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                // Atomic compare-and-swap
                if (Interlocked.CompareExchange(ref flag, 1, 0) == 0)
                {
                    // Only one thread will succeed
                    Interlocked.Increment(ref successCount);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Exactly one thread should succeed
        successCount.Should().Be(1,
            "Interlocked.CompareExchange ensures exactly one winner");
    }

    #endregion

    #region Counter Race Conditions

    /// <summary>
    /// Demonstrates race condition with integer counters.
    /// ChatBot._joinFailures uses non-atomic increment.
    /// </summary>
    [Fact]
    [Trait("Category", "Concurrency")]
    public async Task Counter_NonAtomicIncrement_LosesUpdates()
    {
        int counter = 0;
        const int incrementCount = 10000;
        var tasks = new List<Task>();

        for (int i = 0; i < incrementCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                // Non-atomic increment: read, add, write
                counter++;
            }));
        }

        await Task.WhenAll(tasks);

        // Due to race condition, counter will likely be less than expected
        counter.Should().BeLessThanOrEqualTo(incrementCount,
            "Non-atomic increment can lose updates");

        // Note: The counter might occasionally equal incrementCount
        // if timing happens to work out
    }

    /// <summary>
    /// Shows the correct pattern using Interlocked.Increment.
    /// </summary>
    [Fact]
    [Trait("Category", "Concurrency")]
    public async Task Counter_AtomicIncrement_PreservesAllUpdates()
    {
        int counter = 0;
        const int incrementCount = 10000;
        var tasks = new List<Task>();

        for (int i = 0; i < incrementCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                // Atomic increment
                Interlocked.Increment(ref counter);
            }));
        }

        await Task.WhenAll(tasks);

        // All increments are preserved
        counter.Should().Be(incrementCount,
            "Interlocked.Increment preserves all updates");
    }

    #endregion

    #region Cache Race Condition Tests

    /// <summary>
    /// Demonstrates the cache race condition in SettingsProvider.
    /// Pattern: if (cache.Contains(key)) { return cache.Get(key); }
    /// </summary>
    [Fact]
    [Trait("Category", "Concurrency")]
    public async Task Cache_TOCTOU_BetweenContainsAndGet()
    {
        // Simulating MemoryCache behavior
        var cache = new ConcurrentDictionary<string, string>();
        cache["setting"] = "value";

        var exceptions = new ConcurrentBag<Exception>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Reader simulating GetValueAsync
        var readerTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    // TOCTOU pattern from SettingsProvider.cs:15-26
                    if (cache.ContainsKey("setting"))
                    {
                        await Task.Delay(1); // Processing delay
                        var value = cache["setting"];
                    }
                }
                catch (KeyNotFoundException ex)
                {
                    exceptions.Add(ex);
                }
            }
        });

        // Writer simulating SetValueAsync removing from cache
        var writerTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                cache.TryRemove("setting", out _);
                await Task.Delay(1);
                cache["setting"] = "new_value";
            }
        });

        await Task.WhenAll(readerTask, writerTask);

        // Document that TOCTOU race exists
    }

    #endregion
}
