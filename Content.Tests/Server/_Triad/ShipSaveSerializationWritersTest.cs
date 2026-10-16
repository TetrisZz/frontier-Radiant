using System.Collections.Generic;
using Content.Server._Triad.Shipyard;
using Content.Shared.Lathe;
using Content.Shared.StationRecords;
using NUnit.Framework;
using Robust.Shared.Serialization.Markdown.Sequence;

namespace Content.Tests.Server._Triad;

[TestFixture]
public sealed class ShipSaveSerializationWritersTest
{
    [Test]
    public void ClearingExportedFabricationQueueDoesNotClearLiveQueue()
    {
        var batch = new LatheRecipeBatch("CableStack", 1, 5);
        var queue = new LinkedList<LatheRecipeBatch>();
        queue.AddLast(batch);

        var node = new ShipSaveSerializationWriters().Write(null!, queue, null!);

        Assert.That(node, Is.TypeOf<SequenceDataNode>());
        Assert.That((SequenceDataNode) node, Is.Empty);
        Assert.That(queue, Has.Count.EqualTo(1));
        Assert.That(queue.First!.Value, Is.SameAs(batch));
        Assert.That(batch.ItemsPrinted, Is.EqualTo(1));
        Assert.That(batch.ItemsRequested, Is.EqualTo(5));
    }

    [Test]
    public void ClearingExportedRecordFilterDoesNotChangeLiveFilter()
    {
        var filter = new StationRecordsFilter(StationRecordFilterType.Job, "Engineer");

        var node = new ShipSaveSerializationWriters().Write(null!, filter, null!);

        Assert.That(node.IsNull, Is.True);
        Assert.That(filter.Type, Is.EqualTo(StationRecordFilterType.Job));
        Assert.That(filter.Value, Is.EqualTo("Engineer"));
    }
}
