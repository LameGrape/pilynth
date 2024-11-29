#pragma warning disable CS8602, CS8600, CS8604, CS8605, CS0649, CS8603

using System.IO.Compression;
using System.Reflection;
using System.Text;
using Pilynth.Attributes;
using Kermalis.EndianBinaryIO;
using Pilynth.Fabric;
using System.Xml.Linq;

namespace Pilynth;

public class Mod
{
    public Mod(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Version number is required");
            return;
        }
        string mcVersion = args[0];

        Console.WriteLine($"Building mod for {mcVersion}");

        YarnMappings.PrepareMappings(mcVersion);

        Type[] types = Assembly.GetEntryAssembly().GetExportedTypes();
        List<JavaClass> classes = new();
        string identifier = "", version = "", entrypoint = "";
        JavaClass entryClass = null;

        JavaClass.LogLevel logLevel = JavaClass.LogLevel.None;
        if (args.Length > 1 && args[1].StartsWith("--"))
        {
            if (args[1].Contains("p")) logLevel |= JavaClass.LogLevel.ConstantPool;
            if (args[1].Contains("b")) logLevel |= JavaClass.LogLevel.Bytecode;
            if (args[1].Contains("c")) logLevel |= JavaClass.LogLevel.Classes;
            if (args[1].Contains("i")) logLevel |= JavaClass.LogLevel.Internals;
        }

        List<Type> items = new();
        foreach (Type type in types)
        {
            JavaClass clazz = new JavaClass(type, logLevel);
            classes.Add(clazz);
            if (typeof(FabricMod).IsAssignableFrom(type))
            {
                identifier = type.GetCustomAttribute<IdentifierAttribute>().identifier;
                version = type.GetCustomAttribute<VersionAttribute>().version;
                entrypoint = type.FullName;
                entryClass = clazz;
            }
            if (typeof(Item).IsAssignableFrom(type)) items.Add(type);
        }

        entryClass.HandleMethod(typeof(Mod).GetMethod("_RegisterItem", BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), entryClass.originalType, true);

        foreach (Type item in items)
        {
            JavaClass.MethodReference methodRef = JavaClass.GetMethodReference(typeof(Mod), "_RegisterItem", 3);
            methodRef.classRef = JavaClass.GetClassReference(entryClass.originalType);
            entryClass.ExtendInitializeMethod(JavaClass.StringToBytes(
                "2A"
                + $"""12{BitConverter.ToString([(byte)entryClass.AddToConstants(JavaClass.GetClassReference(item))])}"""
                + $"""12{BitConverter.ToString([(byte)entryClass.AddToConstants(new JavaClass.StringReference { text = identifier })])}"""
                + $"""12{BitConverter.ToString([(byte)entryClass.AddToConstants(new JavaClass.StringReference { text = item.GetCustomAttribute<IdentifierAttribute>().identifier })])}"""
                + $"""B6{BitConverter.ToString(BitConverter.GetBytes(JavaClass.FlipBytes(entryClass.AddToConstants(methodRef))))}"""
                + "B1"
            ), 4);
        }

        JavaArchive jar = new JavaArchive(identifier, version, entrypoint, mcVersion, classes.ToArray());
        if (!Directory.Exists("build")) Directory.CreateDirectory("build");
        Console.WriteLine($"Exporting to build/{identifier}-v{version}-{mcVersion}.jar");
        jar.WriteJarFile($"build/{identifier}-v{version}-{mcVersion}.jar");
    }

    internal void _RegisterItem(Java.Class type, string ns, string id)
    {
        RegistryKey key = RegistryKey.of(RegistryKey.ofRegistry(Identifier.of("item")), Identifier.of(ns, id));
        object instance = type.GetConstructor([typeof(Item.Settings)]).NewInstance([new Item.Settings().registryKey(key)]);
        Registry.register(Registries.ITEM, key, instance);
    }

}

internal class YarnMappings
{
    internal static string[]? mappings;
    internal static Dictionary<string, string> cache = new();
    internal static Dictionary<string, string> classCache = new();

    internal static void PrepareMappings(string version)
    {
        if (!Directory.Exists("yarn")) Directory.CreateDirectory("yarn");
        if (File.Exists($"yarn/{version}.yarn"))
        {
            Console.WriteLine($"Found existing yarn mappings for current version");
            mappings = File.ReadAllLines($"yarn/{version}.yarn");
            return;
        }
        Console.WriteLine("Fetching latest mappings for current version");
        XDocument metadata = XDocument.Load("https://maven.fabricmc.net/net/fabricmc/yarn/maven-metadata.xml");
        string yarnVersion = metadata.Descendants("version").Last(element => element.Value.StartsWith(version)).Value;

        Console.WriteLine($"Downloading and decompressing yarn {yarnVersion} mappings");
        using (var client = new HttpClient())
        using (var stream = client.Send(
            new HttpRequestMessage(HttpMethod.Get, $"https://maven.fabricmc.net/net/fabricmc/yarn/{yarnVersion}/yarn-{yarnVersion}-tiny.gz"
            )).Content.ReadAsStream())
        using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
        using (var file = new FileStream($"yarn/{version}.yarn", FileMode.Create))
            gzip.CopyTo(file);

        mappings = File.ReadAllLines($"yarn/{version}.yarn");
    }

    internal static string ConvertClass(string path, bool mojangClasses = false)
    {
        if (cache.ContainsKey(path)) return mojangClasses ? classCache[path] : cache[path];
        string mapped = mappings.First(mapping =>
        {
            string[] split = mapping.Split();
            return split[0] == "CLASS" && split.Last().Replace("/", ".").Replace("$", ".") == path;
        });
        cache.Add(path, mapped.Split()[mapped.Split().Length - 2]);
        classCache.Add(path, mapped.Split()[1]);
        return mojangClasses ? classCache[path] : cache[path];
    }

    internal static string ConvertMethod(MethodBase method)
    {
        var yarnBind = method.GetCustomAttribute<YarnBindAttribute>();
        string path = yarnBind.useNativeName ?
            JavaClass.GetClassBindName(method.DeclaringType) + "." + method.Name :
            yarnBind.name;
        string[] pathSplit = path.Split(".");
        string descriptor = JavaClass.GetMethodDescriptor(method);
        if (cache.ContainsKey(path + descriptor)) return cache[path + descriptor];
        if (!classCache.ContainsKey(JavaClass.GetClassBindName(method.DeclaringType))) JavaClass.GetClassName(method.DeclaringType);
        string mapped = mappings.First(mapping =>
        {
            string[] split = mapping.Split();
            return split[0] == "METHOD"
                && pathSplit.Last() == split.Last()
                && classCache[JavaClass.GetClassBindName(method.DeclaringType)] == split[1]
                && JavaClass.GetMethodDescriptor(method, true) == split[2];
        });
        cache.Add(path + descriptor, mapped.Split()[mapped.Split().Length - 2]);
        return cache[path + descriptor];
    }

    internal static string ConvertField(FieldInfo field)
    {
        var yarnBind = field.GetCustomAttribute<YarnBindAttribute>();
        string path = yarnBind.useNativeName ?
            JavaClass.GetClassBindName(field.DeclaringType) + "." + field.Name :
            yarnBind.name;
        string[] pathSplit = path.Split(".");
        if (cache.ContainsKey(path)) return cache[path];
        if (!classCache.ContainsKey(JavaClass.GetClassBindName(field.DeclaringType))) JavaClass.GetClassName(field.DeclaringType);
        string mapped = mappings.First(mapping =>
        {
            string[] split = mapping.Split();
            return split[0] == "FIELD"
                && pathSplit.Last() == split.Last()
                && classCache[JavaClass.GetClassBindName(field.DeclaringType)] == split[1];
        });
        cache.Add(path, mapped.Split()[mapped.Split().Length - 2]);
        return cache[path];
    }
}

internal class JavaClass
{
    [Flags]
    internal enum LogLevel
    {
        None = 0,
        ConstantPool = 1,
        Bytecode = 2,
        Internals = 4,
        Classes = 8
    }

    internal LogLevel logLevel = LogLevel.None;

    internal List<object> constants = new();
    internal List<JavaField> fields = new();
    internal List<JavaMethod> methods = new();
    internal List<ushort> interfaces = new();
    internal Type originalType;

    internal JavaClass(Type type, LogLevel logLevel)
    {
        this.logLevel = logLevel;

        originalType = type;
        string? thisClass = type.FullName;
        string? superClass = GetClassName(type.BaseType);

        if (logLevel.HasFlag(LogLevel.Classes) || logLevel.HasFlag(LogLevel.Internals) || logLevel.HasFlag(LogLevel.Bytecode))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{thisClass} : {superClass}");
            Console.ForegroundColor = ConsoleColor.White;
        }

        if (type.BaseType.FullName == "System.Object") superClass = "java.lang.Object";
        AddToConstants(new ClassReference { name = thisClass.Replace(".", "/") });
        AddToConstants(new ClassReference { name = superClass.Replace(".", "/") });

        foreach (Type inter in type.GetInterfaces())
            interfaces.Add(AddToConstants(new ClassReference { name = GetClassName(inter) }));

        foreach (FieldInfo field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
        {
            if (field.DeclaringType != type) continue;
            if (logLevel == LogLevel.Internals || logLevel == LogLevel.Bytecode)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"    Field {field.DeclaringType}.{field.Name}");
                Console.ForegroundColor = ConsoleColor.White;
            }

            JavaField javaField = new JavaField
            {
                accessFlags = GetFieldAccessFlags(field),
                nameIndex = AddToConstants(field.Name),
                descriptorIndex = AddToConstants(TypeToLetter(field.FieldType))
            };
            fields.Add(javaField);
        }

        foreach (MethodInfo method in type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
        {
            HandleMethod(method, type);
        }
        HandleMethod(type.GetConstructors()[0], type);
    }

    internal struct ConvertedMethod
    {
        internal byte[] code;
        internal ushort maxStack;
    }

    internal void HandleMethod(MethodBase method, Type type, bool synthetic = false)
    {
        if (method.DeclaringType != type && !synthetic) return;
        if (logLevel.HasFlag(LogLevel.Internals) || logLevel.HasFlag(LogLevel.Bytecode))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    Method {type}.{method.Name}");
            Console.ForegroundColor = ConsoleColor.White;
        }

        MethodBody? body = method.GetMethodBody();

        string name = GetMethodName(method);
        string descriptor = GetMethodDescriptor(method);
        JavaMethod javaMethod = new JavaMethod
        {
            accessFlags = GetMethodAccessFlags(method, synthetic),
            nameIndex = AddToConstants(name),
            descriptorIndex = AddToConstants(descriptor)
        };

        ConvertedMethod converted = ConvertMethod(method);
        CodeAttribute attribute = new CodeAttribute
        {
            length = (uint)(2 * 2 + 4 * 2 + converted.code.Length), // 3 shorts, 2 ints, and code length
            maxStack = converted.maxStack,
            maxLocals = (ushort)(body.LocalVariables.Count + method.GetParameters().Length + 1),
            codeLength = (uint)converted.code.Length,
            code = converted.code
        };
        attribute.nameIndex = AddToConstants("Code");
        javaMethod.codeAttribute = attribute;

        methods.Add(javaMethod);
    }

    internal ushort AddToConstants(object item)
    {
        if (!constants.Contains(item))
        {
            if (item is ClassReference) AddToConstants(((ClassReference)item).name);
            if (item is StringReference) AddToConstants(((StringReference)item).text);
            if (item is InterfaceMethodReference)
            {
                AddToConstants(((InterfaceMethodReference)item).classRef);
                AddToConstants(((InterfaceMethodReference)item).nameTypeDesc);
            }
            if (item is MethodReference)
            {
                AddToConstants(((MethodReference)item).classRef);
                AddToConstants(((MethodReference)item).nameTypeDesc);
            }
            if (item is FieldReference)
            {
                AddToConstants(((FieldReference)item).classRef);
                AddToConstants(((FieldReference)item).nameTypeDesc);
            }
            if (item is NameTypeReference)
            {
                AddToConstants(((NameTypeReference)item).name);
                AddToConstants(((NameTypeReference)item).descriptor);
            }
            constants.Add(item);
        }
        return (ushort)(constants.IndexOf(item) + 1);
    }

    internal void ExtendInitializeMethod(byte[] extension, ushort maxStack)
    {
        foreach (JavaMethod method in methods)
        {
            if (((string)constants[method.nameIndex - 1]) != "onInitialize") return;
            method.codeAttribute.maxStack = Math.Max(maxStack, method.codeAttribute.maxStack);
            method.codeAttribute.code = method.codeAttribute.code.AsSpan(0, method.codeAttribute.code.Length - 1).ToArray().Concat(extension).ToArray();
            method.codeAttribute.codeLength = (uint)method.codeAttribute.code.Length;
            method.codeAttribute.length = (uint)(2 * 2 + 4 * 2 + method.codeAttribute.code.Length);
        }
    }

    internal static string GetMethodName(MethodBase method)
    {
        if (method.GetCustomAttribute<EntryPointAttribute>() != null) return "main";
        JavaBindAttribute bind = method.GetCustomAttribute<JavaBindAttribute>();
        if (bind != null) return bind.name.Split(".").Last();
        YarnBindAttribute yarnBind = method.GetCustomAttribute<YarnBindAttribute>();
        if (yarnBind != null) return YarnMappings.ConvertMethod(method);
        if (method.Name == ".ctor") return "<init>";
        return method.Name;
    }

    internal static string GetClassName(Type type, bool mojangClasses = false)
    {
        JavaBindAttribute bind = type.GetCustomAttribute<JavaBindAttribute>(false);
        if (bind != null) return bind.name.Replace(".", "/");
        YarnBindAttribute yarnBind = type.GetCustomAttribute<YarnBindAttribute>(false);
        if (yarnBind != null)
        {
            if (!yarnBind.useNativeName) return YarnMappings.ConvertClass(yarnBind.name, mojangClasses);
            return YarnMappings.ConvertClass(GetClassName(type.DeclaringType) + "." + type.Name, mojangClasses);
        }
        return type.FullName.Replace(".", "/");
    }

    internal static string GetClassBindName(Type type)
    {
        JavaBindAttribute bind = type.GetCustomAttribute<JavaBindAttribute>(false);
        if (bind != null) return bind.name;
        YarnBindAttribute yarnBind = type.GetCustomAttribute<YarnBindAttribute>(false);
        if (yarnBind != null)
        {
            if (!yarnBind.useNativeName) return yarnBind.name;
            return GetClassBindName(type.DeclaringType) + "." + type.Name;
        }
        return type.FullName;
    }

    internal static string GetFieldName(FieldInfo field)
    {
        JavaBindAttribute bind = field.GetCustomAttribute<JavaBindAttribute>();
        if (bind != null) return bind.name.Replace(".", "/");
        YarnBindAttribute yarnBind = field.GetCustomAttribute<YarnBindAttribute>();
        if (yarnBind != null) return YarnMappings.ConvertField(field);
        return field.Name;
    }

    internal static MethodReference GetMethodReference(Type type, string method, int paramCount)
    {
        MethodBase realMethod;
        if (method == ".ctor") realMethod = type.GetConstructors().First(m => m.GetParameters().Length == paramCount);
        else realMethod = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).First(m => m.Name == method && m.GetParameters().Length == paramCount);
        return new MethodReference()
        {
            classRef = new ClassReference { name = ConvertBuiltinName(realMethod.DeclaringType) },
            nameTypeDesc = new NameTypeReference { name = GetMethodName(realMethod), descriptor = GetMethodDescriptor(realMethod) }
        };
    }

    internal static FieldReference GetFieldReference(Type type, string field)
    {
        FieldInfo reaField = type.GetField(field);
        return new FieldReference()
        {
            classRef = new ClassReference { name = ConvertBuiltinName(reaField.DeclaringType) },
            nameTypeDesc = new NameTypeReference { name = GetFieldName(reaField), descriptor = TypeToLetter(reaField.FieldType) }
        };
    }

    internal static ClassReference GetClassReference(Type type)
    {
        return new ClassReference()
        {
            name = GetClassName(type)
        };
    }

    internal byte[] GetConstantBytes(object constant)
    {
        return BitConverter.GetBytes(FlipBytes(AddToConstants(constant)));
    }

    internal static ushort FlipBytes(ushort number)
    {
        return BitConverter.ToUInt16(BitConverter.GetBytes(number).Reverse().ToArray());
    }

    internal static byte[] StringToBytes(string hex)
    {
        hex = hex.Replace("-", "");
        return hex.Chunk(2).Select(x => { return Convert.ToByte(new string(x), 16); }).ToArray();
    }

    [Flags]
    internal enum MethodAccessFlags
    {
        Public = 0x0001,
        Private = 0x0002,
        Protected = 0x0004,
        Static = 0x0008,
        Final = 0x0010,
        Synchronized = 0x0020,
        Bridge = 0x0040,
        VarArgs = 0x0080,
        Native = 0x0100,
        Abstract = 0x0400,
        Strict = 0x0800,
        Synthetic = 0x1000
    }

    [Flags]
    internal enum FieldAccessFlags
    {
        Public = 0x0001,
        Private = 0x0002,
        Protected = 0x0004,
        Static = 0x0008,
        Final = 0x0010,
        Volatile = 0x0040,
        Transient = 0x0080,
        Synthetic = 0x1000,
        Enum = 0x4000
    }

    internal static MethodAccessFlags GetMethodAccessFlags(MethodBase method, bool synthetic = false)
    {
        MethodAccessFlags flags = 0;
        if (method.IsPublic) flags |= MethodAccessFlags.Public;
        if (method.IsPrivate) flags |= MethodAccessFlags.Private;
        if (method.IsFamily) flags |= MethodAccessFlags.Protected;
        if (method.IsStatic) flags |= MethodAccessFlags.Static;
        if (method.IsAbstract) flags |= MethodAccessFlags.Abstract;
        if (synthetic) flags |= MethodAccessFlags.Synthetic;
        return flags;
    }
    internal static FieldAccessFlags GetFieldAccessFlags(FieldInfo field)
    {
        FieldAccessFlags flags = 0;
        if (field.IsPublic || field.DeclaringType.IsInterface) flags |= FieldAccessFlags.Public;
        if (field.IsPrivate && !field.DeclaringType.IsInterface) flags |= FieldAccessFlags.Private;
        if (field.IsFamily && !field.DeclaringType.IsInterface) flags |= FieldAccessFlags.Protected;
        if (field.IsStatic || field.DeclaringType.IsInterface) flags |= FieldAccessFlags.Static;
        if (field.IsInitOnly || field.DeclaringType.IsInterface) flags |= FieldAccessFlags.Final;
        if (field.DeclaringType.IsEnum && !field.DeclaringType.IsInterface) flags |= FieldAccessFlags.Enum;
        return flags;
    }

    internal class JavaMethod
    {
        internal MethodAccessFlags accessFlags;
        internal ushort nameIndex;
        internal ushort descriptorIndex;
        internal CodeAttribute codeAttribute;
    }

    internal struct JavaField
    {
        internal FieldAccessFlags accessFlags;
        internal ushort nameIndex;
        internal ushort descriptorIndex;
    }

    internal struct JavaAttribute
    {
        internal ushort nameIndex;
        internal uint length;
        internal byte[] info;
    }

    internal struct CodeAttribute
    {
        internal ushort nameIndex;
        internal uint length;
        internal ushort maxStack;
        internal ushort maxLocals;
        internal uint codeLength;
        internal byte[] code;
    }

    internal struct ClassReference
    {
        internal string name;
        public override string ToString()
        {
            return name;
        }
    }

    internal struct StringReference
    {
        internal string text;
        public override string ToString()
        {
            return text;
        }
    }

    internal struct InterfaceMethodReference
    {
        internal ClassReference classRef;
        internal NameTypeReference nameTypeDesc;
        public override string ToString()
        {
            return classRef + " - " + nameTypeDesc;
        }
    }

    internal struct FieldReference
    {
        internal ClassReference classRef;
        internal NameTypeReference nameTypeDesc;
        public override string ToString()
        {
            return classRef + " - " + nameTypeDesc;
        }
    }

    internal struct MethodReference
    {
        internal ClassReference classRef;
        internal NameTypeReference nameTypeDesc;
        public override string ToString()
        {
            return classRef + " - " + nameTypeDesc;
        }
    }

    internal struct NameTypeReference
    {
        internal string name;
        internal string descriptor;
        public override string ToString()
        {
            return name + " - " + descriptor;
        }
    }

    internal enum Opcodes
    {
        nop = 0x00,
        _break = 0x01,
        ldarg_0 = 0x02,
        ldarg_1 = 0x03,
        ldarg_2 = 0x04,
        ldarg_3 = 0x05,
        ldarg_s = 0x0E, // <uint8>
        ldloc_0 = 0x06,
        ldloc_1 = 0x07,
        ldloc_2 = 0x08,
        ldloc_3 = 0x09,
        ldloc_s = 0x11, // <uint8>
        stloc_0 = 0x0A,
        stloc_1 = 0x0B,
        stloc_2 = 0x0C,
        stloc_3 = 0x0D,
        stloc_s = 0x13, // <uint8>
        ldc_r4 = 0x22, // <float32>
        dup = 0x25,
        call = 0x28, // <int32>
        callvirt = 0x6F, // <int32>
        ret = 0x2A,
        br_s = 0x2B, // <int8>
        add = 0x58,
        conv_i4 = 0x69,
        box = 0x8C, // <int32>
        ldstr = 0x72,
        newobj = 0x73, // <int32>
        stfld = 0x7D, // <int32>
        stsfld = 080, // <int32>
        ldfld = 0x7B, // <int32>
        ldsfld = 0x7E, // <int32>
        pop = 0x26,
        ldc_i4_0 = 0x16,
        ldc_i4_1 = 0x17,
        ldc_i4_2 = 0x18,
        ldc_i4_3 = 0x19,
        ldc_i4_4 = 0x1A,
        ldc_i4_5 = 0x1B,
        ldc_i4_6 = 0x1C,
        ldc_i4_7 = 0x1D,
        ldc_i4_8 = 0x1E,
        newarr = 0x8D,
        ldtoken = 0xD0,
        stelem_ref = 0xA2,
    }

    internal struct Stackpoint
    {
        internal int ji;
        internal int depth;
    }

    private ConvertedMethod ConvertMethod(MethodBase method)
    {
        MethodBody? body = method.GetMethodBody();
        byte[]? ilcode = body.GetILAsByteArray();

        Module module = method.DeclaringType.Module;

        string returnType = method is MethodInfo ? (method as MethodInfo).ReturnType.Name : "Void";
        int oldStack = 0;
        Stack<string> stack = new Stack<string>(body.MaxStackSize);
        ushort maxStack = (ushort)body.MaxStackSize;
        List<Stackpoint> stackpoints = [new Stackpoint { ji = 0, depth = 0 }]; // locations in the bytecode where items were added to the stack
        List<byte> bytecode = new List<byte>();
        string[] locals = new string[body.LocalVariables.Count + method.GetParameters().Length + 1];

        int runtimeHandle = 0;
        int ji = 0;

        for (int i = 0; i < ilcode.Length; i++)
        {
            Opcodes opcode = (Opcodes)ilcode[i];
            byte newOpcode = 0;
            byte[] args = [];
            byte[] newArgs = [];
            string debug = "";
            bool valid = true;

            int offset;
            string item; // both are used later so i just define them here
            FieldInfo resolvedField;
            try
            {
                switch (opcode)
                {
                    case Opcodes.nop:
                        newOpcode = 0;
                        break;
                    case Opcodes._break:
                        newOpcode = 0xCA;
                        break;
                    case Opcodes.ldarg_0:
                    case Opcodes.ldarg_1:
                    case Opcodes.ldarg_2:
                    case Opcodes.ldarg_3:
                    case Opcodes.ldarg_s:
                        offset = (byte)(opcode - Opcodes.ldarg_0);
                        if (opcode == Opcodes.ldarg_s) offset = ilcode[++i];
                        item = offset > 0 ? method.GetParameters()[offset - 1].ParameterType.Name : "this";
                        if (item == "Int32") newOpcode = offset > 3 ? (byte)0x15 : (byte)(0x1A + offset);
                        else if (item == "Int64") newOpcode = offset > 3 ? (byte)0x16 : (byte)(0x1E + offset);
                        else if (item == "Single") newOpcode = offset > 3 ? (byte)0x17 : (byte)(0x22 + offset);
                        else if (item == "Double") newOpcode = offset > 3 ? (byte)0x18 : (byte)(0x26 + offset);
                        else newOpcode = offset > 3 ? (byte)0x19 : (byte)(0x2A + offset);
                        if (opcode == Opcodes.ldarg_0) newOpcode = 0x2A;
                        if (offset > 3) newArgs = [(byte)offset];
                        stack.Push(opcode == Opcodes.ldarg_0 ? method.DeclaringType.Name : method.GetParameters()[offset - 1].Name);
                        stackpoints.Add(new Stackpoint { ji = ji, depth = oldStack });
                        debug += " +sp";
                        break;
                    case Opcodes.ldloc_0:
                    case Opcodes.ldloc_1:
                    case Opcodes.ldloc_2:
                    case Opcodes.ldloc_3:
                    case Opcodes.ldloc_s:
                        offset = (byte)(opcode - Opcodes.ldloc_0);
                        if (opcode == Opcodes.ldloc_s) offset = ilcode[++i];
                        offset += method.GetParameters().Length + 1;
                        item = locals[offset];
                        if (item == "Int32") newOpcode = offset > 3 ? (byte)0x15 : (byte)(0x1A + offset);
                        else if (item == "Int64") newOpcode = offset > 3 ? (byte)0x16 : (byte)(0x1E + offset);
                        else if (item == "Single") newOpcode = offset > 3 ? (byte)0x17 : (byte)(0x22 + offset);
                        else if (item == "Double") newOpcode = offset > 3 ? (byte)0x18 : (byte)(0x26 + offset);
                        else newOpcode = offset > 3 ? (byte)0x19 : (byte)(0x2A + offset);
                        if (offset > 3) newArgs = [(byte)offset];
                        stack.Push(item);
                        stackpoints.Add(new Stackpoint { ji = ji, depth = oldStack });
                        debug += " +sp";
                        break;
                    case Opcodes.stloc_0:
                    case Opcodes.stloc_1:
                    case Opcodes.stloc_2:
                    case Opcodes.stloc_3:
                    case Opcodes.stloc_s:
                        offset = (byte)(opcode - Opcodes.stloc_0);
                        if (opcode == Opcodes.stloc_s) offset = ilcode[++i];
                        item = stack.Pop();
                        locals[offset] = item;
                        offset += method.GetParameters().Length + 1;
                        if (item == "Int32") newOpcode = offset > 3 ? (byte)0x36 : (byte)(0x3B + offset);
                        else if (item == "Int64") newOpcode = offset > 3 ? (byte)0x37 : (byte)(0x3F + offset);
                        else if (item == "Single") newOpcode = offset > 3 ? (byte)0x38 : (byte)(0x43 + offset);
                        else if (item == "Double") newOpcode = offset > 3 ? (byte)0x39 : (byte)(0x47 + offset);
                        else newOpcode = offset > 3 ? (byte)0x3A : (byte)(0x4B + offset);
                        if (offset > 3) newArgs = [(byte)offset];
                        break;
                    case Opcodes.ldc_r4:
                        newOpcode = 0x12;
                        stack.Push("Single");
                        stackpoints.Add(new Stackpoint { ji = ji, depth = oldStack });
                        debug += " +sp";
                        args = new ArraySegment<byte>(ilcode, i + 1, 4).ToArray();
                        float constant = BitConverter.ToSingle(args);
                        newArgs = [(byte)AddToConstants(constant)];
                        i += 4;
                        break;
                    case Opcodes.ldc_i4_0:
                    case Opcodes.ldc_i4_1:
                    case Opcodes.ldc_i4_2:
                    case Opcodes.ldc_i4_3:
                    case Opcodes.ldc_i4_4:
                    case Opcodes.ldc_i4_5:
                    case Opcodes.ldc_i4_6:
                    case Opcodes.ldc_i4_7:
                    case Opcodes.ldc_i4_8:
                        offset = (byte)(opcode - Opcodes.ldc_i4_0);
                        newOpcode = 0x12;
                        stack.Push("Integer");
                        stackpoints.Add(new Stackpoint { ji = ji, depth = oldStack });
                        debug += " +sp";
                        newArgs = [(byte)AddToConstants(offset)];
                        break;
                    case Opcodes.dup:
                        newOpcode = 0x59;
                        stack.Push(stack.Peek());
                        break;
                    case Opcodes.pop:
                        newOpcode = 0x57;
                        stack.Pop();
                        break;
                    case Opcodes.ldstr:
                        newOpcode = 0x12;
                        args = new ArraySegment<byte>(ilcode, i + 1, 4).ToArray();
                        string resolvedString = module.ResolveString(BitConverter.ToInt32(args));
                        debug = resolvedString;
                        newArgs = [(byte)AddToConstants(new StringReference { text = resolvedString })];
                        stack.Push("String");
                        stackpoints.Add(new Stackpoint { ji = ji, depth = oldStack });
                        debug += " +sp";
                        i += 4;
                        break;
                    case Opcodes.call:
                    case Opcodes.callvirt:
                        args = new ArraySegment<byte>(ilcode, i + 1, 4).ToArray();
                        MethodBase resolvedMethod = module.ResolveMethod(BitConverter.ToInt32(args));
                        string name = GetMethodName(resolvedMethod);
                        debug = name;
                        newOpcode = resolvedMethod.IsStatic ? (byte)0xB8 : resolvedMethod.IsPrivate || resolvedMethod.IsConstructor ? (byte)0xB7 : (byte)0xB6;
                        newOpcode = opcode == Opcodes.call ? newOpcode : (byte)0xB6;
                        if (resolvedMethod.Name == "GetTypeFromHandle")
                        {
                            newOpcode = 0x12;
                            Type resolved = module.ResolveType(runtimeHandle);
                            newArgs = [(byte)AddToConstants(new ClassReference { name = ConvertBuiltinName(resolved) })];
                            stack.Pop();
                            debug = ConvertBuiltinName(resolved);
                            i += 4;
                            break;
                        }
                        if (resolvedMethod.DeclaringType.IsInterface)
                        {
                            if (!resolvedMethod.IsStatic) newOpcode = 0xB9;
                            InterfaceMethodReference methodRef = new InterfaceMethodReference()
                            {
                                classRef = new ClassReference { name = ConvertBuiltinName(resolvedMethod.DeclaringType) },
                                nameTypeDesc = new NameTypeReference { name = GetMethodName(resolvedMethod), descriptor = GetMethodDescriptor(resolvedMethod) }
                            };
                            newArgs = BitConverter.GetBytes(FlipBytes(AddToConstants(methodRef)));
                            if (!resolvedMethod.IsStatic) newArgs = [newArgs[0], newArgs[1], (byte)(resolvedMethod.GetParameters().Length + 1), 0];
                        }
                        else
                        {
                            MethodReference methodRef = new MethodReference()
                            {
                                classRef = new ClassReference { name = ConvertBuiltinName(resolvedMethod.DeclaringType) },
                                nameTypeDesc = new NameTypeReference { name = GetMethodName(resolvedMethod), descriptor = GetMethodDescriptor(resolvedMethod) }
                            };
                            newArgs = BitConverter.GetBytes(FlipBytes(AddToConstants(methodRef)));
                        }
                        foreach (var param in resolvedMethod.GetParameters()) stack.Pop();
                        if (resolvedMethod is MethodInfo && (resolvedMethod as MethodInfo).ReturnType != typeof(void))
                        {
                            stack.Push((resolvedMethod as MethodInfo).ReturnType.Name);
                            stackpoints.Add(new Stackpoint { ji = ji, depth = oldStack });
                            debug += " +sp";
                        }
                        i += 4;
                        break;
                    case Opcodes.ret:
                        if (returnType == "Int32") newOpcode = 0xAC;
                        else if (returnType == "Int64") newOpcode = 0xAD;
                        else if (returnType == "Single") newOpcode = 0xAE;
                        else if (returnType == "Double") newOpcode = 0xAF;
                        else if (returnType == "Void") newOpcode = 0xB1;
                        else newOpcode = 0xB0;
                        break;
                    case Opcodes.br_s:
                        newOpcode = 0xA7;
                        args = [ilcode[i + 1], 0];
                        newArgs = args;
                        break;
                    case Opcodes.add:
                        item = stack.Pop();
                        stack.Pop();
                        if (item == "Int32") newOpcode = 0x60;
                        if (item == "Int64") newOpcode = 0x61;
                        if (item == "Single") newOpcode = 0x62;
                        if (item == "Double") newOpcode = 0x63;
                        stack.Push(item);
                        stackpoints.Add(new Stackpoint { ji = ji, depth = oldStack });
                        debug += " +sp";
                        break;
                    case Opcodes.conv_i4:
                        item = stack.Pop();
                        if (item == "Int64") newOpcode = 0x88;
                        if (item == "Single") newOpcode = 0x8B;
                        if (item == "Double") newOpcode = 0x8E;
                        stack.Push("Int32");
                        stackpoints.Add(new Stackpoint { ji = ji, depth = oldStack });
                        debug += " +sp";
                        break;
                    case Opcodes.box:
                        newOpcode = 0xB8;
                        args = new ArraySegment<byte>(ilcode, i + 1, 4).ToArray();
                        Type type = module.ResolveType(BitConverter.ToInt32(args));
                        MethodReference _methodRef = new MethodReference()
                        {
                            classRef = new ClassReference() { name = ConvertBuiltinName(type) },
                            nameTypeDesc = new NameTypeReference() { name = "valueOf", descriptor = $"({TypeToLetter(type)})L{ConvertBuiltinName(type)};" }
                        };
                        newArgs = BitConverter.GetBytes(FlipBytes(AddToConstants(_methodRef)));
                        debug = ConvertBuiltinName(type) + ".valueof";
                        i += 4;
                        break;
                    case Opcodes.newobj:
                        args = new ArraySegment<byte>(ilcode, i + 1, 4).ToArray();
                        Type resolvedType = module.ResolveMethod(BitConverter.ToInt32(args)).DeclaringType;
                        newArgs = BitConverter.GetBytes(FlipBytes(AddToConstants(new ClassReference() { name = ConvertBuiltinName(resolvedType) })));
                        MethodReference _evenMoreMethodRef = new MethodReference()
                        {
                            classRef = new ClassReference() { name = ConvertBuiltinName(resolvedType) },
                            nameTypeDesc = new NameTypeReference() { name = "<init>", descriptor = GetMethodDescriptor(resolvedType.GetConstructors()[0]) }
                        };
                        debug = ConvertBuiltinName(resolvedType);
                        stack.Push(resolvedType.Name);
                        // java is stupid and requires manual constructor invocation
                        int paramCount = resolvedType.GetConstructors()[0].GetParameters().Length;
                        if (paramCount == 0)
                        {
                            newArgs = newArgs.Concat([(byte)0x59, (byte)0xB7]).Concat(BitConverter.GetBytes(FlipBytes(AddToConstants(_evenMoreMethodRef)))).ToArray();
                            newOpcode = 0xBB;
                            stackpoints.Add(new Stackpoint { ji = ji, depth = oldStack });
                            debug += " +sp";
                        }
                        else
                        {
                            byte[] stupidCrap = [0xBB];
                            stupidCrap = stupidCrap.Concat(newArgs).ToArray();
                            stupidCrap = stupidCrap.Concat([(byte)0x59]).ToArray();
                            bytecode.InsertRange(
                                stackpoints.Last(sp => sp.ji < ji && sp.depth == oldStack - paramCount - 1).ji,
                                stupidCrap);
                            newOpcode = 0xB7;
                            newArgs = BitConverter.GetBytes(FlipBytes(AddToConstants(_evenMoreMethodRef)));
                            foreach (var param in resolvedType.GetConstructors()[0].GetParameters()) stack.Pop();
                        }
                        maxStack++;
                        i += 4;
                        break;
                    case Opcodes.stfld:
                    case Opcodes.stsfld:
                        args = new ArraySegment<byte>(ilcode, i + 1, 4).ToArray();
                        resolvedField = module.ResolveField(BitConverter.ToInt32(args));
                        newArgs = BitConverter.GetBytes(FlipBytes(AddToConstants(new FieldReference()
                        {
                            classRef = new ClassReference { name = ConvertBuiltinName(resolvedField.DeclaringType) },
                            nameTypeDesc = new NameTypeReference { name = GetFieldName(resolvedField), descriptor = TypeToLetter(resolvedField.FieldType) }
                        })));
                        newOpcode = resolvedField.IsStatic ? (byte)0xB3 : (byte)0xB5;
                        stack.Pop();
                        stack.Pop();
                        debug = GetFieldName(resolvedField);
                        i += 4;
                        break;
                    case Opcodes.ldfld:
                    case Opcodes.ldsfld:
                        args = new ArraySegment<byte>(ilcode, i + 1, 4).ToArray();
                        resolvedField = module.ResolveField(BitConverter.ToInt32(args));
                        newArgs = BitConverter.GetBytes(FlipBytes(AddToConstants(new FieldReference()
                        {
                            classRef = new ClassReference { name = ConvertBuiltinName(resolvedField.DeclaringType) },
                            nameTypeDesc = new NameTypeReference { name = GetFieldName(resolvedField), descriptor = TypeToLetter(resolvedField.FieldType) }
                        })));
                        newOpcode = resolvedField.IsStatic ? (byte)0xB2 : (byte)0xB4;
                        debug = GetFieldName(resolvedField);
                        stack.Push(resolvedField.FieldType.Name);
                        stackpoints.Add(new Stackpoint { ji = ji, depth = oldStack });
                        debug += " +sp";
                        i += 4;
                        break;
                    case Opcodes.newarr:
                        newOpcode = 0xBD;
                        args = new ArraySegment<byte>(ilcode, i + 1, 4).ToArray();
                        resolvedType = module.ResolveType(BitConverter.ToInt32(args));
                        newArgs = BitConverter.GetBytes(FlipBytes(AddToConstants(new ClassReference { name = ConvertBuiltinName(resolvedType) })));
                        debug = ConvertBuiltinName(resolvedType) + "[]";
                        stack.Push("Array");
                        stackpoints.Add(new Stackpoint { ji = ji, depth = oldStack });
                        debug += " +sp";
                        i += 4;
                        break;
                    case Opcodes.ldtoken:
                        args = new ArraySegment<byte>(ilcode, i + 1, 4).ToArray();
                        runtimeHandle = BitConverter.ToInt32(args);
                        i += 4;
                        break;
                    case Opcodes.stelem_ref:
                        newOpcode = 0x53;
                        break;
                    default:
                        if (logLevel.HasFlag(LogLevel.Bytecode))
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.Error.WriteLine($"      Unimplemented opcode: {BitConverter.ToString([(byte)opcode])}");
                            Console.ForegroundColor = ConsoleColor.White;
                        }
                        valid = false;
                        break;
                }
            }
            catch
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error in opcode: {opcode} ({debug})");
                throw;
            }
            ji += 1 + newArgs.Length;
            if (valid)
            {
                bytecode.Add(newOpcode);
                foreach (byte arg in newArgs) bytecode.Add(arg);
                if (logLevel.HasFlag(LogLevel.Bytecode))
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write($"        {oldStack}:{stack.Count} ");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"{Enum.GetName(opcode)}{(args.Length > 0 ? " " : "")}{BitConverter.ToString(args)}");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(" > ");
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.Write($"{BitConverter.ToString([newOpcode])} {BitConverter.ToString(newArgs)}");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write($"{(debug.Length > 0 ? " (" : "")}{debug}{(debug.Length > 0 ? ")" : "")}\n");
                    Console.ForegroundColor = ConsoleColor.White;
                }
                oldStack = stack.Count;
            }
        }
        return new ConvertedMethod { code = bytecode.ToArray(), maxStack = maxStack };
    }

    internal static string TypeToLetter(Type type, bool mojangClasses = false)
    {
        return type.Name switch
        {
            "Int32" => "I",
            "Single" => "F",
            "Double" => "D",
            "Char" => "C",
            "Byte" => "B",
            "Int64" => "L",
            "Boolean" => "Z",
            "Int16" => "S",
            "Void" => "V",
            "Object" => "Ljava/lang/Object;",
            "Type" => "Ljava/lang/Class;",
            _ => (type.IsArray ? "[" : "") + "L" + ConvertBuiltinName(type, mojangClasses).Replace(".", "/").Replace("[]", "") + ";"

        };
    }

    internal static string GetMethodDescriptor(MethodBase method, bool mojangClasses = false)
    {
        ParameterInfo[] parameters = method.GetParameters();
        string descriptor = "(";
        foreach (ParameterInfo parameter in parameters)
        {
            Type type = parameter.ParameterType;
            if (type is null) continue;
            descriptor += TypeToLetter(type, mojangClasses);
        }
        descriptor += ")" + TypeToLetter(method is MethodInfo ? (method as MethodInfo).ReturnType : typeof(void), mojangClasses);
        return descriptor;
    }

    internal static string ConvertBuiltinName(Type type, bool mojangClasses = false)
    {
        if (type.FullName is null) return type.Name;
        return type.Name.Replace("[]", "") switch
        {
            "Int32" => "java/lang/Integer",
            "Single" => "java/lang/Float",
            "Char" => "java/lang/Character",
            "Int64" => "java/lang/Long",
            "Int16" => "java/lang/Short",
            "String" => "java/lang/String",
            "Object" => "java/lang/Object",
            "Type" => "java/lang/Class",
            _ => GetClassName(type, mojangClasses)
        };
    }

    internal void WriteClassFile(Stream stream)
    {
        if (logLevel.HasFlag(LogLevel.ConstantPool))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Writing " + originalType.FullName);
        }
        var writer = new EndianBinaryWriter(stream, endianness: Endianness.BigEndian);
        writer.WriteUInt32(0xCAFEBABE); // magic number
        writer.WriteUInt32(52); // major version
        writer.WriteUInt16((ushort)(constants.Count + 1)); // constant pool count
        ushort i = 0;
        foreach (object constant in constants)
        {
            if (logLevel.HasFlag(LogLevel.ConstantPool))
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write(BitConverter.ToString(BitConverter.GetBytes(FlipBytes((ushort)(i + 1)))) + ": ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(constant.ToString() + " : ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(constant.GetType().Name + "\n");
                Console.ForegroundColor = ConsoleColor.White;
            }
            if (constant is int || constant is byte || constant is short)
            {
                writer.WriteByte(3);
                writer.WriteInt32((int)constant);
            }
            if (constant is string)
            {
                string text = constant.ToString().Replace("" + (char)0x00, "" + (char)0xC0 + (char)0x80);
                writer.WriteByte(1);
                writer.WriteUInt16((ushort)Encoding.UTF8.GetBytes(text).Length);
                writer.WriteBytes(Encoding.UTF8.GetBytes(text));
            }
            if (constant is float)
            {
                writer.WriteByte(4);
                writer.WriteSingle((float)constant);
            }
            if (constant is ClassReference)
            {
                ClassReference reference = (ClassReference)constant;
                writer.WriteByte(7);
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.name) + 1));
            }
            if (constant is StringReference)
            {
                StringReference reference = (StringReference)constant;
                writer.WriteByte(8);
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.text) + 1));
            }
            if (constant is InterfaceMethodReference)
            {
                InterfaceMethodReference reference = (InterfaceMethodReference)constant;
                writer.WriteByte(11);
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.classRef) + 1));
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.nameTypeDesc) + 1));
            }
            if (constant is FieldReference)
            {
                FieldReference reference = (FieldReference)constant;
                writer.WriteByte(9);
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.classRef) + 1));
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.nameTypeDesc) + 1));
            }
            if (constant is MethodReference)
            {
                MethodReference reference = (MethodReference)constant;
                writer.WriteByte(10);
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.classRef) + 1));
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.nameTypeDesc) + 1));
            }
            if (constant is NameTypeReference)
            {
                NameTypeReference reference = (NameTypeReference)constant;
                writer.WriteByte(12);
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.name) + 1));
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.descriptor) + 1));
            }
            i++;
        }
        writer.WriteUInt16(0x0020 | 0x0001); // access flags
        writer.WriteUInt16(2); // this class index
        writer.WriteUInt16(4); // super class index
        writer.WriteUInt16((ushort)interfaces.Count);
        foreach (ushort inter in interfaces) writer.WriteUInt16(inter);
        writer.WriteUInt16((ushort)fields.Count); // field count
        foreach (JavaField field in fields) WriteField(writer, field);
        writer.WriteUInt16((ushort)methods.Count); // method count
        foreach (JavaMethod method in methods) WriteMethod(writer, method);
        writer.WriteUInt16(0); // attribute count

        stream.Dispose();
    }

    internal void WriteField(EndianBinaryWriter writer, JavaField field)
    {
        writer.WriteUInt16((ushort)field.accessFlags);
        writer.WriteUInt16(field.nameIndex);
        writer.WriteUInt16(field.descriptorIndex);
        writer.WriteUInt16(0); // attribute count
    }

    internal void WriteMethod(EndianBinaryWriter writer, JavaMethod method)
    {
        writer.WriteUInt16((ushort)method.accessFlags);
        writer.WriteUInt16(method.nameIndex);
        writer.WriteUInt16(method.descriptorIndex);
        writer.WriteUInt16(1); // attribute count
        writer.WriteUInt16(method.codeAttribute.nameIndex);
        writer.WriteUInt32(method.codeAttribute.length);
        writer.WriteUInt16(method.codeAttribute.maxStack);
        writer.WriteUInt16(method.codeAttribute.maxLocals);
        writer.WriteUInt32(method.codeAttribute.codeLength);
        writer.WriteBytes(method.codeAttribute.code);
        writer.WriteUInt16(0); // exception table length
        writer.WriteUInt16(0); // attribute count
    }
}

internal class JavaArchive
{
    internal JavaClass[] classes;
    internal string identifier;
    internal string version;
    internal string entrypoint;
    internal string mcVersion;

    internal JavaArchive(string identifier, string version, string entrypoint, string mcVersion, params JavaClass[] classes)
    {
        this.identifier = identifier;
        this.version = version;
        this.entrypoint = entrypoint;
        this.mcVersion = mcVersion;
        this.classes = classes;
    }

    internal void WriteJarFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        using (var file = new FileStream(path, FileMode.OpenOrCreate))
        {
            using (var archive = new ZipArchive(file, ZipArchiveMode.Update))
            {
                foreach (JavaClass clazz in classes)
                {
                    ZipArchiveEntry entry = archive.CreateEntry(clazz.originalType.FullName.Replace(".", "/") + ".class", CompressionLevel.Fastest);
                    using (Stream stream = entry.Open())
                        clazz.WriteClassFile(stream);
                }
                if (Directory.Exists("assets"))
                {
                    foreach (string assetFile in Directory.GetFiles("assets", "*", SearchOption.AllDirectories))
                    {
                        archive.CreateEntryFromFile(assetFile, assetFile, CompressionLevel.Fastest);
                    }
                }
                ZipArchiveEntry manifest = archive.CreateEntry("META-INF/MANIFEST.MF", CompressionLevel.Fastest);
                using (Stream stream = manifest.Open())
                {
                    stream.Write(Encoding.ASCII.GetBytes(
                        "Manifest-Version: 1.0\n" +
                        "Fabric-Jar-Type: classes\n" +
                        "Fabric-Minecraft-Version: " + mcVersion));
                }
                ZipArchiveEntry modJson = archive.CreateEntry("fabric.mod.json", CompressionLevel.Fastest);
                using (Stream stream = modJson.Open())
                {
                    stream.Write(Encoding.ASCII.GetBytes(
                        $$$"""{"schemaVersion": 1, "id": "{{{identifier}}}","version": "{{{version}}}", "entrypoints": {"main": ["{{{entrypoint}}}"]}}"""
                    ));
                }
            }
        }
    }
}