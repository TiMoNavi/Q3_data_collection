using System;

namespace SObasic
{
    /// <summary>
    /// Attribute to mark interface fields in the Inspector.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class InterfaceAttribute : Attribute
    {
        public Type InterfaceType { get; }

        public InterfaceAttribute(Type interfaceType)
        {
            InterfaceType = interfaceType;
        }
    }
}
