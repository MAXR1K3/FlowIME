using System.Collections.Concurrent;
using FlowIME.Windows.Input;

namespace FlowIME.Windows.Tests.Input;

public sealed class TsfProfileOperationGateTests
{
    [Fact]
    public async Task Run_serializes_concurrent_tsf_operations()
    {
        var active = 0;
        var maximumActive = 0;
        var entered = new ConcurrentQueue<int>();

        var tasks = Enumerable.Range(0, 12)
            .Select(index => Task.Run(() =>
                TsfProfileOperationGate.Run("test", () =>
                {
                    var current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximumActive, current);
                    entered.Enqueue(index);
                    Thread.Sleep(5);
                    Interlocked.Decrement(ref active);
                    return index;
                })))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, maximumActive);
        Assert.Equal(12, entered.Count);
        Assert.Equal(
            Enumerable.Range(0, 12).OrderBy(value => value),
            results.OrderBy(value => value));
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maximum);
            if (observed >= candidate ||
                Interlocked.CompareExchange(ref maximum, candidate, observed) == observed)
            {
                return;
            }
        }
    }
}
