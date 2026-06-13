using System;

namespace SObasic
{
    /// <summary>
    /// Interface for objects that can select from multiple options.
    /// </summary>
    public interface ISelector
    {
        event Action WhenSelected;
        event Action WhenUnselected;
        object GetSelectedValue();
    }
}
