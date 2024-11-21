#pragma warning disable CS8602, CS8600, CS8604, CS8605, CS0649

using System.IO.Compression;
using System.Reflection;
using System.Text;
using Pilynth.Attributes;
using Kermalis.EndianBinaryIO;
using Pilynth.Fabric;

namespace Pilynth;

public class Mod
{
    public Mod(string mcVersion)
    {
        Type[] types = Assembly.GetEntryAssembly().GetExportedTypes();
        List<JavaClass> classes = new();
        string identifier = "", version = "", entrypoint = "";
        foreach (Type type in types)
        {
            classes.Add(new JavaClass(type));
            if (typeof(FabricMod).IsAssignableFrom(type))
            {
                identifier = type.GetCustomAttribute<IdentifierAttribute>().identifier;
                version = type.GetCustomAttribute<VersionAttribute>().version;
                entrypoint = type.FullName;
            }
        }
        JavaArchive jar = new JavaArchive(identifier, version, entrypoint, mcVersion, classes.ToArray());
        if (!Directory.Exists("build")) Directory.CreateDirectory("build");
        System.Console.WriteLine($"build/{identifier}-v{version}-{mcVersion}.jar");
        jar.WriteJarFile($"build/{identifier}-v{version}-{mcVersion}.jar");
    }
}

internal class YarnMappings
{
    //     internal static void PrepareMappings(string version)
    //     {
    //         XDocument metadata = XDocument.Load("https://maven.fabricmc.net/net/fabricmc/yarn/maven-metadata.xml");
    //         string yarnVersion = metadata.Descendants("version").Last(element => element.Value.StartsWith(version)).Value;
    //         System.Console.WriteLine(yarnVersion);
    //     } TODO: download mappings for the specified version instead of being hardcoded

    internal static string ConvertMethod(string method)
    {
        string[] split = method.Split(".");
        string clazz = string.Join('/', split.AsSpan(0, split.Length - 1).ToArray()) + ".mapping";
        if (!File.Exists("yarn/" + clazz)) return method; // i dont know what to tell you buddy the method doesnt exist
        string[] file = File.ReadAllLines("yarn/" + clazz);
        string mojangClass = file[0].Split(" ")[1];
        string mojangMethod = file.First(line => line.Trim().Split(" ")[2] == split.Last()).Split(" ")[1];
        return mojangClass + "/" + mojangMethod;
    }

    internal static string ConvertClass(string clazz)
    {
        clazz = clazz.Replace(".", "/") + ".mapping";
        if (!File.Exists("yarn/" + clazz)) return clazz; // i dont know what to tell you buddy the ~~method~~ class doesnt exist
        string[] file = File.ReadAllLines("yarn/" + clazz);
        string mojangClass = file[0].Split(" ")[1];
        return mojangClass;
    }
}

internal class JavaClass
{
    internal enum LogLevel
    {
        None,
        Full
    }

    internal LogLevel logLevel = LogLevel.Full;

    internal List<object> constants = new();
    internal List<JavaField> fields = new();
    internal List<JavaMethod> methods = new();
    internal List<ushort> interfaces = new();
    internal Type originalType;

    internal JavaClass(Type type)
    {
        originalType = type;
        string? thisClass = type.FullName;
        string? superClass = GetClassName(type.BaseType);

        if (logLevel == LogLevel.Full)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"{thisClass} : {originalType.FullName}");
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
            if (logLevel == LogLevel.Full)
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
        HandleMethod(type.GetConstructor([]), type);
    }

    internal void HandleMethod(MethodBase method, Type type)
    {
        if (method.DeclaringType != type) return;
        if (logLevel == LogLevel.Full)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"    Method {method.DeclaringType}.{method.Name} -");
            Console.ForegroundColor = ConsoleColor.White;
        }

        MethodBody? body = method.GetMethodBody();

        string name = GetMethodName(method);
        string descriptor = GetMethodDescriptor(method);
        JavaMethod javaMethod = new JavaMethod
        {
            accessFlags = GetMethodAccessFlags(method),
            nameIndex = AddToConstants(name),
            descriptorIndex = AddToConstants(descriptor)
        };

        byte[] code = ConvertMethod(method);
        CodeAttribute attribute = new CodeAttribute
        {
            length = (uint)(2 * 2 + 4 * 2 + code.Length), // 3 shorts, 2 ints, and code length
            maxStack = (ushort)body.MaxStackSize,
            maxLocals = (ushort)(body.LocalVariables.Count + method.GetParameters().Length + 1),
            codeLength = (uint)code.Length,
            code = code
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

    internal string GetMethodName(MethodBase method)
    {
        if (method.GetCustomAttribute<EntryPointAttribute>() != null) return "main";
        JavaBindAttribute bind = method.GetCustomAttribute<JavaBindAttribute>();
        if (bind != null) return bind.name.Split(".").Last();
        YarnBindAttribute yarnBind = method.GetCustomAttribute<YarnBindAttribute>();
        if (yarnBind != null) return YarnMappings.ConvertMethod(yarnBind.name.Split("/").Last());
        return method.Name;
    }

    internal string GetMethodClass(MethodBase method)
    {
        JavaBindAttribute bind = method.GetCustomAttribute<JavaBindAttribute>();
        if (bind != null)
        {
            string[] fullName = bind.name.Split(".");
            return string.Join("/", fullName.AsSpan(0, fullName.Length - 1).ToArray());
        }
        YarnBindAttribute yarnBind = method.GetCustomAttribute<YarnBindAttribute>();
        if (yarnBind != null)
        {
            string[] fullName = yarnBind.name.Split("/");
            return string.Join("/", fullName.AsSpan(0, fullName.Length - 1).ToArray());
        }
        return method.DeclaringType.FullName.Replace(".", "/");
    }

    internal string GetClassName(Type type)
    {
        JavaBindAttribute bind = type.GetCustomAttribute<JavaBindAttribute>();
        if (bind != null) return bind.name.Replace(".", "/");
        YarnBindAttribute yarnBind = type.GetCustomAttribute<YarnBindAttribute>();
        if (yarnBind != null) return YarnMappings.ConvertClass(yarnBind.name);
        return type.FullName.Replace(".", "/");
    }

    internal ushort FlipBytes(ushort number)
    {
        return BitConverter.ToUInt16(BitConverter.GetBytes(number).Reverse().ToArray());
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

    internal MethodAccessFlags GetMethodAccessFlags(MethodBase method)
    {
        MethodAccessFlags flags = 0;
        if (method.IsPublic) flags |= MethodAccessFlags.Public;
        if (method.IsPrivate) flags |= MethodAccessFlags.Private;
        if (method.IsFamily) flags |= MethodAccessFlags.Protected;
        if (method.IsStatic) flags |= MethodAccessFlags.Static;
        if (method.IsAbstract) flags |= MethodAccessFlags.Abstract;
        return flags;
    }
    internal FieldAccessFlags GetFieldAccessFlags(FieldInfo field)
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

    internal struct JavaMethod
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
    }

    private byte[] ConvertMethod(MethodBase method)
    {
        MethodBody? body = method.GetMethodBody();
        byte[]? ilcode = body.GetILAsByteArray();

        Module module = method.DeclaringType.Module;

        string returnType = method is MethodInfo ? (method as MethodInfo).ReturnType.Name : "Void";
        Stack<string> stack = new Stack<string>(body.MaxStackSize);
        List<byte> bytecode = new List<byte>();
        string[] locals = new string[body.LocalVariables.Count + method.GetParameters().Length + 1];

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
                    item = locals[offset];
                    if (item == "Int32") newOpcode = offset > 3 ? (byte)0x15 : (byte)(0x1A + offset);
                    if (item == "Int64") newOpcode = offset > 3 ? (byte)0x16 : (byte)(0x1E + offset);
                    if (item == "Single") newOpcode = offset > 3 ? (byte)0x17 : (byte)(0x22 + offset);
                    if (item == "Double") newOpcode = offset > 3 ? (byte)0x18 : (byte)(0x26 + offset);
                    if (opcode == Opcodes.ldarg_0) newOpcode = 0x2A;
                    if (offset > 3) newArgs = [(byte)offset];
                    stack.Push(item);
                    break;
                case Opcodes.ldloc_0:
                case Opcodes.ldloc_1:
                case Opcodes.ldloc_2:
                case Opcodes.ldloc_3:
                case Opcodes.ldloc_s:
                    offset = (byte)(opcode - Opcodes.ldloc_0);
                    if (opcode == Opcodes.ldloc_s) offset = ilcode[++i];
                    offset += method.GetParameters().Length;
                    item = locals[offset];
                    if (item == "Int32") newOpcode = offset > 3 ? (byte)0x15 : (byte)(0x1A + offset);
                    if (item == "Int64") newOpcode = offset > 3 ? (byte)0x16 : (byte)(0x1E + offset);
                    if (item == "Single") newOpcode = offset > 3 ? (byte)0x17 : (byte)(0x22 + offset);
                    if (item == "Double") newOpcode = offset > 3 ? (byte)0x18 : (byte)(0x26 + offset);
                    if (offset > 3) newArgs = [(byte)offset];
                    stack.Push(item);
                    break;
                case Opcodes.stloc_0:
                case Opcodes.stloc_1:
                case Opcodes.stloc_2:
                case Opcodes.stloc_3:
                case Opcodes.stloc_s:
                    offset = (byte)(opcode - Opcodes.stloc_0);
                    if (opcode == Opcodes.stloc_s) offset = ilcode[++i];
                    if (opcode != Opcodes.stloc_0) offset += method.GetParameters().Length;
                    item = stack.Pop();
                    if (item == "Int32") newOpcode = offset > 3 ? (byte)0x36 : (byte)(0x3B + offset);
                    if (item == "Int64") newOpcode = offset > 3 ? (byte)0x37 : (byte)(0x3F + offset);
                    if (item == "Single") newOpcode = offset > 3 ? (byte)0x38 : (byte)(0x43 + offset);
                    if (item == "Double") newOpcode = offset > 3 ? (byte)0x39 : (byte)(0x47 + offset);
                    if (offset > 3) newArgs = [(byte)offset];
                    break;
                case Opcodes.ldc_r4:
                    newOpcode = 0x12;
                    stack.Push("Single");
                    args = new ArraySegment<byte>(ilcode, i + 1, 4).ToArray();
                    float constant = BitConverter.ToSingle(args);
                    newArgs = [(byte)AddToConstants(constant)];
                    i += 4;
                    break;
                case Opcodes.dup:
                    newOpcode = 0x59;
                    stack.Push(stack.Peek());
                    break;
                case Opcodes.ldstr:
                    newOpcode = 0x12;
                    args = new ArraySegment<byte>(ilcode, i + 1, 4).ToArray();
                    string resolvedString = module.ResolveString(BitConverter.ToInt32(args));
                    debug = resolvedString;
                    newArgs = [(byte)AddToConstants(new StringReference { text = resolvedString })];
                    i += 4;
                    break;
                case Opcodes.call:
                case Opcodes.callvirt:
                    args = new ArraySegment<byte>(ilcode, i + 1, 4).ToArray();
                    MethodBase resolvedMethod = module.ResolveMethod(BitConverter.ToInt32(args));
                    string name = GetMethodName(resolvedMethod);
                    debug = name;
                    newOpcode = resolvedMethod.IsStatic ? (byte)0xB8 : (byte)0xB7;
                    newOpcode = opcode == Opcodes.call ? newOpcode : (byte)0xB6;
                    if (resolvedMethod.DeclaringType.IsInterface)
                    {
                        newOpcode = 0xB9;
                        InterfaceMethodReference methodRef = new InterfaceMethodReference()
                        {
                            classRef = new ClassReference { name = GetMethodClass(resolvedMethod) },
                            nameTypeDesc = new NameTypeReference { name = GetMethodName(resolvedMethod), descriptor = GetMethodDescriptor(resolvedMethod) }
                        };
                        newArgs = BitConverter.GetBytes(FlipBytes(AddToConstants(methodRef)));
                        newArgs = [newArgs[0], newArgs[1], (byte)(resolvedMethod.GetParameters().Length + 1), 0];
                    }
                    else
                    {
                        MethodReference methodRef = new MethodReference()
                        {
                            classRef = new ClassReference { name = GetMethodClass(resolvedMethod) },
                            nameTypeDesc = new NameTypeReference { name = GetMethodName(resolvedMethod), descriptor = GetMethodDescriptor(resolvedMethod) }
                        };
                        newArgs = BitConverter.GetBytes(FlipBytes(AddToConstants(methodRef)));
                    }
                    if (resolvedMethod is MethodInfo && (resolvedMethod as MethodInfo).ReturnType != typeof(void))
                        stack.Push((resolvedMethod as MethodInfo).ReturnType.Name);
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
                    break;
                case Opcodes.conv_i4:
                    item = stack.Pop();
                    if (item == "Int64") newOpcode = 0x88;
                    if (item == "Single") newOpcode = 0x8B;
                    if (item == "Double") newOpcode = 0x8E;
                    stack.Push("Int32");
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
                    newOpcode = 0xBB;
                    stack.Push("Reference");
                    i += 4;
                    break;
                case Opcodes.stfld:
                    args = new ArraySegment<byte>(ilcode, i + 1, 4).ToArray();
                    FieldInfo resolvedField = module.ResolveField(BitConverter.ToInt32(args));
                    newArgs = BitConverter.GetBytes(FlipBytes(AddToConstants(new FieldReference()
                    {
                        classRef = new ClassReference { name = GetClassName(resolvedField.DeclaringType) },
                        nameTypeDesc = new NameTypeReference { name = resolvedField.Name, descriptor = TypeToLetter(resolvedField.FieldType) }
                    })));
                    newOpcode = 0xB5;
                    i += 4;
                    break;
                default:
                    if (logLevel == LogLevel.Full)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Error.WriteLine($"      Unimplemented opcode: {BitConverter.ToString([(byte)opcode])}");
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                    valid = false;
                    break;
            }
            if (valid)
            {
                bytecode.Add(newOpcode);
                foreach (byte arg in newArgs) bytecode.Add(arg);
                if (logLevel == LogLevel.Full)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"        {Enum.GetName(opcode)}{(args.Length > 0 ? " " : "")}{BitConverter.ToString(args)}");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(" > ");
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.Write($"{BitConverter.ToString([newOpcode])} {BitConverter.ToString(newArgs)}");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write($"{(debug.Length > 0 ? " (" : "")}{debug}{(debug.Length > 0 ? ")" : "")}\n");
                    Console.ForegroundColor = ConsoleColor.White;
                }
            }
        }
        return bytecode.ToArray();
    }

    internal string TypeToLetter(Type type)
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
            _ => (type.IsArray ? "[" : "") + "L" + ConvertBuiltinName(type).Replace(".", "/").Replace("[]", "") + ";"

        };
    }

    internal string GetMethodDescriptor(MethodBase method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        string descriptor = "(";
        foreach (ParameterInfo parameter in parameters)
        {
            Type type = parameter.ParameterType;
            if (type is null) continue;
            descriptor += TypeToLetter(type);
        }
        descriptor += ")" + TypeToLetter(method is MethodInfo ? (method as MethodInfo).ReturnType : typeof(void));
        return descriptor;
    }

    internal string ConvertBuiltinName(Type type)
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
            _ => GetClassName(type)
        };
    }

    internal void WriteClassFile(Stream stream)
    {
        var writer = new EndianBinaryWriter(stream, endianness: Endianness.BigEndian);
        writer.WriteUInt32(0xCAFEBABE); // magic number
        writer.WriteUInt32(52); // major version
        writer.WriteUInt16((ushort)(constants.Count + 1)); // constant pool count
        foreach (object constant in constants)
        {
            if (logLevel == LogLevel.Full)
            {
                Console.Write(constant.ToString() + " : ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(constant.GetType().Name + "\n");
                Console.ForegroundColor = ConsoleColor.White;
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
        }
        writer.WriteUInt16(0b00100001); // access flags
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
        using (var file = new FileStream(path, FileMode.OpenOrCreate))
        {
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                foreach (JavaClass clazz in classes)
                {
                    ZipArchiveEntry entry = archive.CreateEntry(clazz.originalType.FullName.Replace(".", "/") + ".class");
                    using (Stream stream = entry.Open())
                        clazz.WriteClassFile(stream);
                }
                ZipArchiveEntry manifest = archive.CreateEntry("META-INF/MANIFEST.MF");
                using (Stream stream = manifest.Open())
                {
                    stream.Write(Encoding.ASCII.GetBytes(
                        "Manifest-Version: 1.0\n" +
                        "Fabric-Jar-Type: classes\n" +
                        "Fabric-Minecraft-Version: " + mcVersion));
                }
                ZipArchiveEntry modJson = archive.CreateEntry("fabric.mod.json");
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