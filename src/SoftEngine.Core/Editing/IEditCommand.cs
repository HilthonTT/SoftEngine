namespace SoftEngine.Core.Editing;

public interface IEditCommand
{
    string Description { get; }

    void Apply();

    void Revert();
}
