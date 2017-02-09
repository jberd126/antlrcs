#if PORTABLE && !NETCF

namespace System 
{
    [AttributeUsage(AttributeTargets.Field, Inherited=false)]
    internal sealed class NonSerializedAttribute : Attribute 
    {
    }
}

#endif
