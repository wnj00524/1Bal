using System.Numerics;
using TacticalSim.Core.Cover;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Materials;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.World;
using Xunit;

namespace TacticalSim.Tests;

public sealed class WorldBoundsTests
{
    [Fact]
    public void CreateDefault_UsesHouseScenarioDimensions()
    {
        WorldBounds bounds = WorldBounds.CreateDefault();

        Assert.Equal(new Vector3(-50f, 0f, -50f), bounds.Min);
        Assert.Equal(new Vector3(50f, 30f, 50f), bounds.Max);
        Assert.Equal(new Vector3(100f, 30f, 100f), bounds.Size);
        Assert.Equal(new Vector3(0f, 15f, 0f), bounds.Centre);
        Assert.Equal(300_000f, bounds.Volume);
    }

    [Theory]
    [InlineData(0f, 0f, 0f, true)]
    [InlineData(-50f, 30f, 50f, true)]
    [InlineData(50.01f, 10f, 0f, false)]
    [InlineData(0f, -0.01f, 0f, false)]
    public void Contains_IncludesBoundaryOnly(float x, float y, float z, bool expected) =>
        Assert.Equal(expected, WorldBounds.CreateDefault().Contains(new Vector3(x, y, z)));

    [Fact]
    public void Clamp_RestrictsEveryAxis() =>
        Assert.Equal(new Vector3(50f, 0f, -50f),
            WorldBounds.CreateDefault().Clamp(new Vector3(100f, -2f, -80f)));

    [Fact]
    public void Constructor_RejectsInvalidOrNonFiniteCorners()
    {
        Assert.Throws<ArgumentException>(() => new WorldBounds(Vector3.One, Vector3.Zero));
        Assert.Throws<ArgumentException>(() =>
            new WorldBounds(new Vector3(float.NaN, 0f, 0f), Vector3.One));
    }

    [Fact]
    public void EqualBounds_HaveValueEquality()
    {
        WorldBounds first = WorldBounds.CreateDefault();
        WorldBounds second = WorldBounds.CreateDefault();
        Assert.Equal(first, second);
        Assert.True(first == second);
    }
}

public sealed class TacticalWorldTests
{
    [Fact]
    public void AddAndMoveEntity_ClampPositionAndRaiseEvents()
    {
        var world = new TacticalWorld(WorldBounds.CreateDefault());
        var entity = CreateEntity(new Vector3(500f, 50f, -500f));
        int additions = 0;
        world.EntityAdded += (_, args) =>
        {
            additions++;
            Assert.Same(entity, args.Entity);
        };

        world.AddEntity(entity);
        Assert.Equal(new Vector3(50f, 30f, -50f), entity.Position);
        world.SetEntityPosition(entity.Id, new Vector3(-70f, -1f, 70f));

        Assert.Equal(new Vector3(-50f, 0f, 50f), entity.Position);
        Assert.Same(entity, world.GetEntity(entity.Id));
        Assert.Equal(1, additions);
    }

    [Fact]
    public void RemoveEntity_RaisesEventAndRemovesEntity()
    {
        var world = new TacticalWorld(WorldBounds.CreateDefault());
        var entity = CreateEntity(Vector3.Zero);
        world.AddEntity(entity);
        IEntity? removed = null;
        world.EntityRemoved += (_, args) => removed = args.Entity;

        Assert.True(world.RemoveEntity(entity.Id));
        Assert.Same(entity, removed);
        Assert.Null(world.GetEntity(entity.Id));
        Assert.False(world.RemoveEntity(entity.Id));
    }

    [Fact]
    public void GetEntities_IsDeterministicSnapshot()
    {
        var world = new TacticalWorld(WorldBounds.CreateDefault());
        world.AddEntity(CreateEntity(Vector3.Zero));
        world.AddEntity(CreateEntity(Vector3.One));

        Guid[] ids = world.GetEntities().Select(entity => entity.Id).ToArray();
        Assert.Equal(ids.OrderBy(id => id), ids);
    }

    [Fact]
    public void CoverSurfaces_AreStoredByWorld()
    {
        var world = new TacticalWorld(WorldBounds.CreateDefault());
        var cover = new CoverPolygon(
            [Vector3.Zero, Vector3.UnitX, Vector3.UnitX + Vector3.UnitY, Vector3.UnitY],
            0.2f,
            MaterialType.Wood);

        world.AddCoverSurface(cover);

        Assert.Same(cover, Assert.Single(world.GetCoverSurfaces()));
    }

    [Fact]
    public void InvalidOperations_ThrowMeaningfulExceptions()
    {
        var world = new TacticalWorld(WorldBounds.CreateDefault());
        Assert.Throws<ArgumentNullException>(() => world.AddEntity(null!));
        Assert.Throws<ArgumentNullException>(() => world.AddCoverSurface(null!));
        Assert.Throws<KeyNotFoundException>(() => world.SetEntityPosition(Guid.NewGuid(), Vector3.Zero));
        var entity = CreateEntity(Vector3.Zero);
        world.AddEntity(entity);
        Assert.Throws<ArgumentException>(() => world.SetEntityPosition(entity.Id, new Vector3(float.NaN)));
    }

    private static TacticalEntity CreateEntity(Vector3 position) =>
        new(position, new TacticalActorPhysiology());
}
