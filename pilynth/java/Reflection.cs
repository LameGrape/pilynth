using Pilynth.Attributes;

namespace Pilynth.Java;

[JavaBind("java.lang.Class")]
public class Class
{
    public Class(Type type) { }

    [JavaBind("java.lang.Class.getConstructor")]
    public extern Constructor GetConstructor(Type[] parameters);
}

[JavaBind("java.lang.reflect.Constructor")]
public class Constructor
{
    [JavaBind("java.lang.reflect.Constructor.newInstance")]
    public extern object NewInstance(object[] parameters);
}