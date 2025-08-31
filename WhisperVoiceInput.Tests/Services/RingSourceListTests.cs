using DynamicData;
using FluentAssertions;
using NUnit.Framework;
using WhisperVoiceInput.Services.LogViewer;

namespace WhisperVoiceInput.Tests.Services;

[TestFixture]
public class RingSourceListTests
{
    [Test]
    public void Add_UpToCapacity_EmitsAdds()
    {
        var list = new RingSourceList<int>(3);
        var changes = new List<IChangeSet<int>>();
        using var sub = list.Connect().Subscribe(changes.Add);

        list.Edit(e => { e.Add(1); e.Add(2); e.Add(3); });

        changes.Should().NotBeEmpty();
        var flat = changes.SelectMany(c => c).ToList();
        flat.Count(c => c.Reason == ListChangeReason.Add).Should().Be(3);
        flat.Last().Item.Current.Should().Be(3);
    }

    [Test]
    public void Add_WhenAtCapacity_EvictsHead_EmitsRemoveAndAdd()
    {
        var list = new RingSourceList<int>(3);
        var changes = new List<IChangeSet<int>>();
        using var sub = list.Connect().Subscribe(changes.Add);

        list.Edit(e => { e.Add(1); e.Add(2); e.Add(3); });
        changes.Clear();

        list.Edit(e => e.Add(4));

        var flat = changes.SelectMany(c => c).ToList();
        flat.Should().ContainSingle(c => c.Reason == ListChangeReason.Remove && c.Item.Current == 1);
        flat.Should().ContainSingle(c => c.Reason == ListChangeReason.Add && c.Item.Current == 4);
    }

    [Test]
    public void UpdateCapacity_Shrink_EmitsRemoveRange()
    {
        var list = new RingSourceList<int>(3);
        var changes = new List<IChangeSet<int>>();
        using var sub = list.Connect().Subscribe(changes.Add);

        list.Edit(e => { e.Add(1); e.Add(2); e.Add(3); });
        changes.Clear();

        list.UpdateCapacity(2);

        var flat = changes.SelectMany(c => c).ToList();
        flat.Should().ContainSingle(c => c.Reason == ListChangeReason.RemoveRange);
        var rr = flat.Single(c => c.Reason == ListChangeReason.RemoveRange);
        rr.Range.Should().NotBeNull();
        rr.Range!.ToList().Should().ContainSingle(i => i == 1);
    }

    [Test]
    public void Connect_WithExistingItems_EmitsInitialAddRange()
    {
        var list = new RingSourceList<int>(3);
        list.Edit(e => { e.Add(1); e.Add(2); });

        var changes = new List<IChangeSet<int>>();
        using var sub = list.Connect().Subscribe(changes.Add);

        var flat = changes.SelectMany(c => c).ToList();
        flat.Should().ContainSingle(c => c.Reason == ListChangeReason.AddRange);
        var ar = flat.Single(c => c.Reason == ListChangeReason.AddRange);
        ar.Range.Should().NotBeNull();
        ar.Range!.ToList().Should().ContainInOrder(1, 2);
    }

    [Test]
    public void CountChanged_And_IsEmptyChanged_Work()
    {
        var list = new RingSourceList<int>(2);

        var counts = new List<int>();
        var empties = new List<bool>();
        using var csub = list.CountChanged.Subscribe(counts.Add);
        using var esub = list.IsEmptyChanged.Subscribe(empties.Add);

        list.Edit(e => e.Add(1));
        list.Edit(e => e.Add(2));
        list.Edit(e => e.Add(3)); // evict one, count stays 2

        counts.Should().ContainInOrder(0, 1, 2, 2);
        empties.First().Should().BeTrue();
        empties.Last().Should().BeFalse();
    }

    [Test]
    public void AddRange_EmitsAddRange_AndRemovesIfNeeded()
    {
        var list = new RingSourceList<int>(3);
        var changes = new List<IChangeSet<int>>();
        using var sub = list.Connect().Subscribe(changes.Add);

        list.Edit(e => e.AddRange(new[] { 1, 2, 3 }));
        changes.SelectMany(c => c).Any(c => c.Reason == ListChangeReason.AddRange).Should().BeTrue();

        changes.Clear();
        list.Edit(e => e.AddRange(new[] { 4, 5 }));

        var flat = changes.SelectMany(c => c).ToList();
        flat.Any(c => c.Reason == ListChangeReason.RemoveRange).Should().BeTrue();
        flat.Any(c => c.Reason == ListChangeReason.AddRange).Should().BeTrue();
    }
}


