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

internal class JavaClass
{
    internal enum LogLevel
    {
        None,
        Full
    }

    internal LogLevel logLevel = LogLevel.None;

    internal List<object> constants = new();
    internal List<JavaMethod> methods = new();
    internal List<ushort> interfaces = new();
    internal Type originalType;

    internal JavaClass(Type type)
    {
        originalType = type;
        string? thisClass = type.FullName;
        if (thisClass is null) throw new NullReferenceException("for some reason the class has no fullname idk");
        Type? baseClass = type.BaseType;
        if (baseClass is null) throw new NullReferenceException("the class literally doesnt inhereit from anything how?!");
        string? superClass = GetClassName(baseClass);
        if (superClass is null) throw new NullReferenceException("what the heck same thing with the base class it has no fullname");
        // TODO: make better error handling

        if (logLevel == LogLevel.Full)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n--- Converting {thisClass} : {superClass} ---");
            Console.ForegroundColor = ConsoleColor.White;
        }

        if (baseClass.FullName == "System.Object") superClass = "java.lang.Object";
        AddToConstants(new ClassReference { name = thisClass.Replace(".", "/") });
        AddToConstants(new ClassReference { name = superClass.Replace(".", "/") });

        foreach (Type inter in type.GetInterfaces())
            interfaces.Add(AddToConstants(new ClassReference { name = GetClassName(inter) }));

        foreach (MethodInfo method in type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
        {
            if (method.DeclaringType != type) continue;
            if (logLevel == LogLevel.Full)
            {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine($"\n- Converting method {method.DeclaringType}.{method.Name} -");
                Console.ForegroundColor = ConsoleColor.White;
            }

            MethodBody? body = method.GetMethodBody();
            if (body is null) throw new NullReferenceException($"Method body is null. Too bad!");

            string name = GetMethodName(method);
            string descriptor = GetMethodDescriptor(method);
            JavaMethod javaMethod = new JavaMethod
            {
                accessFlags = GetAccessFlags(method)
            };
            javaMethod.nameIndex = AddToConstants(name);
            javaMethod.descriptorIndex = AddToConstants(descriptor);

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
        ushort methodRef = AddToConstants(new MethodReference
        {
            classRef = new ClassReference
            {
                name = "java/lang/Object"
            },
            nameTypeDesc = new NameTypeReference
            {
                name = "<init>",
                descriptor = "()V"
            }
        });
        byte[] indexShort = BitConverter.GetBytes(FlipBytes(methodRef));
        JavaMethod initMethod = new JavaMethod
        {
            nameIndex = AddToConstants("<init>"),
            descriptorIndex = AddToConstants("()V"),
            accessFlags = AccessFlags.Public,
            codeAttribute = new CodeAttribute
            {
                nameIndex = AddToConstants("Code"),
                code = [0x2A, 0xB7, indexShort[0], indexShort[1], 0xB1], // aload_0, invokespecial, return
                length = 2 * 2 + 4 * 2 + 5,
                maxStack = 1,
                maxLocals = 1,
                codeLength = 5
            }
        };
        methods.Add(initMethod);
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
        return method.DeclaringType.FullName.Replace(".", "/");
    }

    internal string GetClassName(Type type)
    {
        JavaBindAttribute bind = type.GetCustomAttribute<JavaBindAttribute>();
        if (bind != null) return bind.name.Replace(".", "/");
        return type.FullName.Replace(".", "/");
    }

    internal ushort FlipBytes(ushort number)
    {
        return BitConverter.ToUInt16(BitConverter.GetBytes(number).Reverse().ToArray());
    }

    [Flags]
    internal enum AccessFlags
    {
        Public = 1,
        Private = 2,
        Protected = 4,
        Static = 8,
        Final = 16,
        Synchronized = 32,
        Bridge = 64,
        VarArgs = 128,
        Native = 256,
        Abstract = 512,
        Strict = 1024,
        Synthetic = 2048
    }

    internal AccessFlags GetAccessFlags(MethodInfo method)
    {
        AccessFlags flags = 0;
        if (method.IsPublic) flags |= AccessFlags.Public;
        if (method.IsPrivate) flags |= AccessFlags.Private;
        if (method.IsFamily) flags |= AccessFlags.Protected;
        if (method.IsStatic) flags |= AccessFlags.Static;
        if (method.IsAbstract) flags |= AccessFlags.Abstract;
        return flags;
    }

    internal struct JavaMethod
    {
        internal AccessFlags accessFlags;
        internal ushort nameIndex;
        internal ushort descriptorIndex;
        internal CodeAttribute codeAttribute;
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

    internal class Opcodes
    {
        internal const byte nop = 0x00;
        internal const byte _break = 0x01;
        internal const byte ldloc_0 = 0x06;
        internal const byte ldloc_1 = 0x07;
        internal const byte ldloc_2 = 0x08;
        internal const byte ldloc_3 = 0x09;
        internal const byte stloc_0 = 0x0A;
        internal const byte stloc_1 = 0x0B;
        internal const byte stloc_2 = 0x0C;
        internal const byte stloc_3 = 0x0D;
        internal const byte ldc_r4 = 0x22; // <float32>
        internal const byte dup = 0x25;
        internal const byte call = 0x28; // <int32>
        internal const byte callvirt = 0x6F; // <int32>
        internal const byte ret = 0x2A;
        internal const byte br_s = 0x2B; // <int8>
        internal const byte add = 0x58;
        internal const byte conv_i4 = 0x69;
        internal const byte box = 0x8C; // <int32>
        internal const byte ldstr = 0x72;
    }

    private byte[] ConvertMethod(MethodInfo method)
    {
        MethodBody? body = method.GetMethodBody();
        if (body is null) throw new NullReferenceException("Method body is null womp womp"); // compiler warnings be gone
        byte[]? ilcode = body.GetILAsByteArray();
        if (ilcode is null) throw new NullReferenceException("womp womp no ilcode");

        Module module = method.DeclaringType.Module;

        string returnType = method.ReturnType.Name;
        Stack<string> stack = new Stack<string>(body.MaxStackSize);
        List<byte> bytecode = new List<byte>();
        string[] locals = new string[body.LocalVariables.Count + method.GetParameters().Length + 1];

        for (int i = 0; i < ilcode.Length; i++)
        {
            byte opcode = ilcode[i];
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
                case Opcodes.ldloc_0:
                case Opcodes.ldloc_1:
                case Opcodes.ldloc_2:
                case Opcodes.ldloc_3:
                    offset = opcode - Opcodes.ldloc_0;
                    item = locals[offset];
                    if (item == "Int32") newOpcode = (byte)(0x1A + offset);
                    if (item == "Int64") newOpcode = (byte)(0x1E + offset);
                    if (item == "Single") newOpcode = (byte)(0x22 + offset);
                    if (item == "Double") newOpcode = (byte)(0x26 + offset);
                    stack.Push(item);
                    break;
                case Opcodes.stloc_0:
                case Opcodes.stloc_1:
                case Opcodes.stloc_2:
                case Opcodes.stloc_3:
                    offset = opcode - Opcodes.stloc_0;
                    item = stack.Pop();
                    if (item == "Int32") newOpcode = (byte)(0x3B + offset);
                    if (item == "Int64") newOpcode = (byte)(0x3F + offset);
                    if (item == "Single") newOpcode = (byte)(0x43 + offset);
                    if (item == "Double") newOpcode = (byte)(0x47 + offset);
                    locals[offset] = item;
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
                        newOpcode = (byte)0xB9;
                        InterfaceMethodReference methodRef = new InterfaceMethodReference()
                        {
                            classRef = new ClassReference { name = GetMethodClass(resolvedMethod) },
                            nameTypeDesc = new NameTypeReference { name = GetMethodName(resolvedMethod), descriptor = GetMethodDescriptor(resolvedMethod as MethodInfo) }
                        };
                        newArgs = BitConverter.GetBytes(FlipBytes(AddToConstants(methodRef)));
                        newArgs = [newArgs[0], newArgs[1], (byte)(resolvedMethod.GetParameters().Length + 1), 0];
                    }
                    else
                    {
                        MethodReference methodRef = new MethodReference()
                        {
                            classRef = new ClassReference { name = GetMethodClass(resolvedMethod) },
                            nameTypeDesc = new NameTypeReference { name = GetMethodName(resolvedMethod), descriptor = GetMethodDescriptor(resolvedMethod as MethodInfo) }
                        };
                        newArgs = BitConverter.GetBytes(FlipBytes(AddToConstants(methodRef)));
                    }
                    if ((resolvedMethod as MethodInfo).ReturnType != typeof(void))
                        stack.Push((resolvedMethod as MethodInfo).ReturnType.Name);
                    i += 4;
                    break;
                case Opcodes.ret:
                    if (returnType == "Int32") newOpcode = (byte)0xAC;
                    if (returnType == "Int64") newOpcode = (byte)0xAD;
                    if (returnType == "Single") newOpcode = (byte)0xAE;
                    if (returnType == "Double") newOpcode = (byte)0xAF;
                    if (returnType == "Void") newOpcode = (byte)0xB1;
                    break;
                case Opcodes.br_s:
                    newOpcode = 0xA7;
                    args = [ilcode[i + 1], 0];
                    newArgs = args;
                    break;
                case Opcodes.add:
                    item = stack.Pop();
                    stack.Pop();
                    if (item == "Int32") newOpcode = (byte)0x60;
                    if (item == "Int64") newOpcode = (byte)0x61;
                    if (item == "Single") newOpcode = (byte)0x62;
                    if (item == "Double") newOpcode = (byte)0x63;
                    stack.Push(item);
                    break;
                case Opcodes.conv_i4:
                    item = stack.Pop();
                    if (item == "Int64") newOpcode = (byte)0x88;
                    if (item == "Single") newOpcode = (byte)0x8B;
                    if (item == "Double") newOpcode = (byte)0x8E;
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
                default:
                    if (logLevel == LogLevel.Full)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Error.WriteLine($"Unimplemented opcode: {BitConverter.ToString([opcode])}");
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
                    var fields = typeof(Opcodes).GetFields(BindingFlags.NonPublic | BindingFlags.Static).Where(field => field.IsLiteral);
                    Console.Write($"{fields.First(field => (byte)field.GetRawConstantValue() == opcode).Name}{(args.Length > 0 ? " " : "")}{BitConverter.ToString(args)}");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(" > ");
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.Write($"{BitConverter.ToString([newOpcode])} {BitConverter.ToString(newArgs)}");
                    Console.ForegroundColor = ConsoleColor.Blue;
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

    internal string GetMethodDescriptor(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        string descriptor = "(";
        foreach (ParameterInfo parameter in parameters)
        {
            Type type = parameter.ParameterType;
            if (type is null) continue;
            descriptor += TypeToLetter(type);
        }
        descriptor += ")" + TypeToLetter(method.ReturnType);
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
                writer.WriteByte((byte)1);
                writer.WriteUInt16((ushort)Encoding.UTF8.GetBytes(text).Length);
                writer.WriteBytes(Encoding.UTF8.GetBytes(text));
            }
            if (constant is float)
            {
                writer.WriteByte((byte)4);
                writer.WriteSingle((float)constant);
            }
            if (constant is ClassReference)
            {
                ClassReference reference = (ClassReference)constant;
                writer.WriteByte((byte)7);
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.name) + 1));
            }
            if (constant is StringReference)
            {
                StringReference reference = (StringReference)constant;
                writer.WriteByte((byte)8);
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.text) + 1));
            }
            if (constant is InterfaceMethodReference)
            {
                InterfaceMethodReference reference = (InterfaceMethodReference)constant;
                writer.WriteByte((byte)11);
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.classRef) + 1));
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.nameTypeDesc) + 1));
            }
            if (constant is MethodReference)
            {
                MethodReference reference = (MethodReference)constant;
                writer.WriteByte((byte)10);
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.classRef) + 1));
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.nameTypeDesc) + 1));
            }
            if (constant is NameTypeReference)
            {
                NameTypeReference reference = (NameTypeReference)constant;
                writer.WriteByte((byte)12);
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.name) + 1));
                writer.WriteUInt16((ushort)(constants.IndexOf(reference.descriptor) + 1));
            }
        }
        writer.WriteUInt16(0b00100001); // access flags
        writer.WriteUInt16(2); // this class index
        writer.WriteUInt16(4); // super class index
        writer.WriteUInt16((ushort)interfaces.Count);
        foreach (ushort inter in interfaces) writer.WriteUInt16(inter);
        writer.WriteUInt16(0); // field count TODO: field support
        writer.WriteUInt16((ushort)methods.Count); // method count
        foreach (JavaMethod method in methods) WriteMethod(writer, method);
        writer.WriteUInt16(0); // attribute count

        stream.Dispose();
    }

    internal void WriteMethod(EndianBinaryWriter writer, JavaMethod method)
    {
        writer.WriteUInt16((ushort)method.accessFlags);
        writer.WriteUInt16(method.nameIndex);
        writer.WriteUInt16(method.descriptorIndex);
        writer.WriteUInt16((ushort)1); // attribute count
        writer.WriteUInt16(method.codeAttribute.nameIndex);
        writer.WriteUInt32(method.codeAttribute.length);
        writer.WriteUInt16(method.codeAttribute.maxStack);
        writer.WriteUInt16(method.codeAttribute.maxLocals);
        writer.WriteUInt32(method.codeAttribute.codeLength);
        writer.WriteBytes(method.codeAttribute.code);
        writer.WriteUInt16((ushort)0); // exception table length
        writer.WriteUInt16((ushort)0); // attribute count
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