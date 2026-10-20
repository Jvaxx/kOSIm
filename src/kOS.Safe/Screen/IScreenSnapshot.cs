using System.Collections.Generic;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The screen's displayed rows, top row and cursor at a moment. The list is the
    /// snapshot's own, but a <see cref="ScreenSnapShot"/> taken from a screen holds the
    /// screen's own lines (<see cref="IScreenBuffer.GetBuffer"/> does not copy them), so its
    /// rows show later changes to those lines. <see cref="DeepCopy"/> keeps the text as it is
    /// now, for a later <see cref="DiffFrom"/>; that is also why a diff cannot skip rows by
    /// their identity.
    /// </summary>
    public interface IScreenSnapShot
    {
        List<IScreenBufferLine> Buffer {get;}
        int TopRow {get;}
        int CursorColumn {get;}
        int CursorRow {get;}
        int RowCount {get;}
        
        string DiffFrom(IScreenSnapShot older);
        
        IScreenSnapShot DeepCopy();
    }
}