using ICloudDriveSync.Sync;

namespace ICloudDriveSync.Tests.Sync;

public class LocalChangeCoalescerTests
{
    [Fact]
    public void CoalescesMultipleChangesIntoOneEvent()
    {
        var coalescer = new LocalChangeCoalescer();

        coalescer.Add("a.txt", WatchChange.Created);
        coalescer.Add("a.txt", WatchChange.Changed);
        coalescer.Add("a.txt", WatchChange.Changed);

        var events = coalescer.Drain();

        Assert.Single(events);
        Assert.Equal("a.txt", events[0].Path);
    }

    [Fact]
    public void DeleteWinsOverPreviousChanges()
    {
        var coalescer = new LocalChangeCoalescer();

        coalescer.Add("a.txt", WatchChange.Changed);
        coalescer.Add("a.txt", WatchChange.Deleted);

        var events = coalescer.Drain();

        Assert.Equal(WatchChange.Deleted, events.Single().Change);
    }

    [Fact]
    public void SuppressedEventsAreDropped()
    {
        var coalescer = new LocalChangeCoalescer();
        using (coalescer.Suppress())
        {
            coalescer.Add("a.txt", WatchChange.Changed);
        }

        Assert.Empty(coalescer.Drain());

        // Após o fim da supressão, eventos voltam a ser aceitos.
        coalescer.Add("b.txt", WatchChange.Changed);
        Assert.Single(coalescer.Drain());
    }

    [Fact]
    public void DrainClearsPendingEvents()
    {
        var coalescer = new LocalChangeCoalescer();
        coalescer.Add("a.txt", WatchChange.Changed);

        Assert.Single(coalescer.Drain());
        Assert.Empty(coalescer.Drain());
    }

    [Fact]
    public void SuppressionIsNestedAndReversible()
    {
        var coalescer = new LocalChangeCoalescer();
        var outer = coalescer.Suppress();
        var inner = coalescer.Suppress();

        coalescer.Add("a.txt", WatchChange.Changed);
        inner.Dispose();
        coalescer.Add("b.txt", WatchChange.Changed);

        Assert.Empty(coalescer.Drain());

        outer.Dispose();
        coalescer.Add("c.txt", WatchChange.Changed);
        Assert.Single(coalescer.Drain());
    }
}