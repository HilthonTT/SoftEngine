using SoftEngine.Core.Editing;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Math;
using System.Numerics;

namespace SoftEngine.Core.Tests.Editing;

public class EditHistoryTests
{
    private static TransformEdit MoveTo(IMesh mesh, Vector3 position)
    {
        var before = TransformState.Of(mesh);
        mesh.Position = position;

        return TransformEdit.Between(mesh, before, "Move")
            ?? throw new InvalidOperationException("the move changed nothing");
    }

    [Fact]
    public void Undo_PutsTheMeshBackAndRedoReturnsIt()
    {
        var cube = new Cube();
        var history = new EditHistory();

        history.Push(MoveTo(cube, new Vector3(3f, 0f, 0f)));

        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);

        history.Undo();
        Assert.Equal(Vector3.Zero, cube.Position);

        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);

        history.Redo();
        Assert.Equal(new Vector3(3f, 0f, 0f), cube.Position);
    }

    [Fact]
    public void Undo_OfManyEdits_ReturnsExactlyToTheStart()
    {
        var cube = new Cube();
        var history = new EditHistory();

        for (var i = 1; i <= 20; i++)
        {
            history.Push(MoveTo(cube, new Vector3(i * 0.1f, i * -0.3f, MathF.Sqrt(i))));
        }

        while (history.CanUndo)
        {
            history.Undo();
        }

        Assert.Equal(Vector3.Zero, cube.Position);
    }

    [Fact]
    public void Push_AfterAnUndo_DiscardsTheRedoStack()
    {
        var cube = new Cube();
        var history = new EditHistory();

        history.Push(MoveTo(cube, new Vector3(1f, 0f, 0f)));
        history.Undo();

        Assert.True(history.CanRedo);

        history.Push(MoveTo(cube, new Vector3(0f, 5f, 0f)));

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_Null_IsIgnored()
    {
        var history = new EditHistory();

        history.Push(null);

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Push_BeyondCapacity_DropsTheOldest()
    {
        var cube = new Cube();
        var history = new EditHistory { Capacity = 3 };

        for (var i = 1; i <= 6; i++)
        {
            history.Push(MoveTo(cube, new Vector3(i, 0f, 0f)));
        }

        var undone = 0;
        while (history.Undo() is not null)
        {
            undone++;
        }

        Assert.Equal(3, undone);

        Assert.Equal(new Vector3(3f, 0f, 0f), cube.Position);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        var cube = new Cube();
        var history = new EditHistory();

        history.Push(MoveTo(cube, new Vector3(1f, 0f, 0f)));
        history.Undo();
        history.Push(MoveTo(cube, new Vector3(2f, 0f, 0f)));
        history.Undo();

        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void NextUndo_NamesTheEditThatWouldBeReversed()
    {
        var cube = new Cube();
        var history = new EditHistory();

        history.Push(MoveTo(cube, new Vector3(1f, 0f, 0f)));

        Assert.Equal("Move Cube", history.NextUndo);
        Assert.Null(history.NextRedo);
    }

    [Fact]
    public void Changed_FiresOnEveryStackMovement()
    {
        var cube = new Cube();
        var history = new EditHistory();
        var fired = 0;

        history.Changed += (s, e) => fired++;

        history.Push(MoveTo(cube, new Vector3(1f, 0f, 0f)));
        history.Undo();
        history.Redo();
        history.Clear();

        Assert.Equal(4, fired);
    }

    [Fact]
    public void TransformState_IsNotAliasedToTheMeshsOwnRotation()
    {
        var cube = new Cube();
        cube.Rotation = new Rotation3D(0.1f, 0.2f, 0.3f);

        var before = TransformState.Of(cube);

        cube.Rotation.YYaw = 1.5f;

        Assert.Equal(0.2f, before.Yaw, 6);

        before.ApplyTo(cube);

        Assert.Equal(0.2f, cube.Rotation.YYaw, 6);
    }

    [Fact]
    public void ApplyTo_GivesEachMeshItsOwnRotation()
    {
        var state = new TransformState(Vector3.Zero, Vector3.One, 0.4f, 0f, 0f);

        var first = new Cube();
        var second = new Cube();

        state.ApplyTo(first);
        state.ApplyTo(second);

        first.Rotation.XPitch = 2f;

        Assert.Equal(0.4f, second.Rotation.XPitch, 6);
    }

    [Fact]
    public void Between_WhenNothingChanged_IsNull()
    {
        var cube = new Cube();
        var before = TransformState.Of(cube);

        Assert.Null(TransformEdit.Between(cube, before, "Move"));
    }
}
