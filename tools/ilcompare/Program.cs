// Method-level IL comparison of two .NET assemblies.
// Every method body is rendered as opcodes with operands resolved to names, branch targets
// as instruction indices, locals and exception regions. Compiler-generated ordinals
// (<>c__DisplayClass12_0, <Foo>d__19, <.ctor>b__50_0, <x>5__2 ...) are normalised because
// they shift whenever a method is added or removed elsewhere. Records are compared as
// multisets, so identically-named lambdas still match by body.
//
// Used to prove a rebuild matches a packaged DLL (docs/current-build.md). Only the
// AssemblyInformationalVersion git hash is expected to differ.
//
// Usage: dotnet run --project tools/ilcompare -c Release -- <a.dll> <b.dll> [reportPath]
// Exit code 0 when every type, field and method matches. Set ILCOMPARE_RAW=1 to compare
// compiler-generated names without normalising them (also proves member order matches).
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

static class Program
{
    static readonly Dictionary<ushort, OpCode> Ops = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => (ushort)o.Value, o => o);

    // <Name>d__19, <>c__DisplayClass12_0, <.ctor>b__50_0, <x>5__2, <>9__19_0, <>u__1, <>7__wrap1, <>p__0
    static readonly Regex Ordinal = new(@"(<[^<>]*>[A-Za-z0-9]*__[A-Za-z]*)\d+(_\d+)?", RegexOptions.Compiled);

    static readonly bool Raw = Environment.GetEnvironmentVariable("ILCOMPARE_RAW") == "1";
    public static string Norm(string s) => Raw || s.IndexOf('<') < 0 ? s : Ordinal.Replace(s, "$1#");

    static int Main(string[] args)
    {
        var a = Dump(args[0]);
        var b = Dump(args[1]);
        var report = new StringBuilder();
        report.AppendLine($"A: {args[0]}  ({a.Records.Count} records)");
        report.AppendLine($"B: {args[1]}  ({b.Records.Count} records)");
        report.AppendLine();
        report.AppendLine("== Assembly info (references and assembly attributes)");
        foreach (var l in a.Info.Except(b.Info)) report.AppendLine("  A only: " + l);
        foreach (var l in b.Info.Except(a.Info)) report.AppendLine("  B only: " + l);

        var onlyA = MultisetMinus(a.Records, b.Records);
        var onlyB = MultisetMinus(b.Records, a.Records);
        report.AppendLine();
        report.AppendLine($"== Type/field/method records only in A: {onlyA.Count}, only in B: {onlyB.Count}");

        var keysA = onlyA.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.ToList());
        var keysB = onlyB.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var key in keysA.Keys.Union(keysB.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            keysA.TryGetValue(key, out var ra); keysB.TryGetValue(key, out var rb);
            ra ??= new(); rb ??= new();
            report.AppendLine();
            report.AppendLine($"--- {key}   (A: {ra.Count}, B: {rb.Count})");
            if (ra.Count == 1 && rb.Count == 1)
            {
                foreach (var line in LineDiff(ra[0].Body.Split('\n'), rb[0].Body.Split('\n'))) report.AppendLine("    " + line);
            }
            else
            {
                foreach (var r in ra) { report.AppendLine("  [A]"); foreach (var l in r.Body.Split('\n')) report.AppendLine("    " + l); }
                foreach (var r in rb) { report.AppendLine("  [B]"); foreach (var l in r.Body.Split('\n')) report.AppendLine("    " + l); }
            }
        }
        var text = report.ToString();
        if (args.Length > 2) File.WriteAllText(args[2], text);
        Console.WriteLine(text.Length > 30000 ? text.Substring(0, 30000) + "\n... (truncated, see report file)" : text);
        return onlyA.Count == 0 && onlyB.Count == 0 ? 0 : 1;
    }

    static List<Rec> MultisetMinus(List<Rec> x, List<Rec> y)
    {
        var counts = y.GroupBy(r => r.Full).ToDictionary(g => g.Key, g => g.Count());
        var result = new List<Rec>();
        foreach (var r in x)
        {
            if (counts.TryGetValue(r.Full, out var c) && c > 0) counts[r.Full] = c - 1;
            else result.Add(r);
        }
        return result;
    }

    // Minimal LCS line diff.
    static IEnumerable<string> LineDiff(string[] a, string[] b)
    {
        var n = a.Length; var m = b.Length;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { x++; y++; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) yield return "- " + a[x++];
            else yield return "+ " + b[y++];
        }
        while (x < n) yield return "- " + a[x++];
        while (y < m) yield return "+ " + b[y++];
    }

    record Rec(string Key, string Body) { public string Full => Key + "\n" + Body; }
    record Dumped(List<Rec> Records, List<string> Info);

    static Dumped Dump(string path)
    {
        var pe = new PEReader(File.OpenRead(path));
        var md = pe.GetMetadataReader();
        var names = new Names(md);
        var recs = new List<Rec>();
        var info = new List<string>();
        foreach (var ar in md.AssemblyReferences)
        {
            var r = md.GetAssemblyReference(ar);
            info.Add($"ref {md.GetString(r.Name)} {r.Version}");
        }
        foreach (var ca in md.GetAssemblyDefinition().GetCustomAttributes())
            info.Add("asmattr " + names.Attribute(ca));
        info.Add($"machine {pe.PEHeaders.CoffHeader.Machine} corflags {pe.PEHeaders.CorHeader.Flags}");
        foreach (var rh in md.ManifestResources) info.Add("resource " + md.GetString(md.GetManifestResource(rh).Name));
        info.Add($"typedefs {md.TypeDefinitions.Count} methoddefs {md.MethodDefinitions.Count} fielddefs {md.FieldDefinitions.Count}");

        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            var tname = names.TypeDef(th);
            var tbody = new StringBuilder();
            tbody.Append($"attrs {td.Attributes}; base {(td.BaseType.IsNil ? "-" : names.Type(td.BaseType))}");
            foreach (var ih in td.GetInterfaceImplementations()) tbody.Append("; implements " + names.Type(md.GetInterfaceImplementation(ih).Interface));
            foreach (var ca in td.GetCustomAttributes()) tbody.Append("\n[" + names.Attribute(ca) + "]");
            recs.Add(new Rec("TYPE " + tname, tbody.ToString()));

            foreach (var fh in td.GetFields())
            {
                var f = md.GetFieldDefinition(fh);
                var fb = new StringBuilder($"{f.Attributes} {f.DecodeSignature(names, null)}");
                if (!f.GetDefaultValue().IsNil) fb.Append(" = const");
                foreach (var ca in f.GetCustomAttributes()) fb.Append(" [" + names.Attribute(ca) + "]");
                recs.Add(new Rec($"FIELD {tname}::{Norm(md.GetString(f.Name))}", fb.ToString()));
            }

            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                var sig = m.DecodeSignature(names, null);
                var key = $"METHOD {tname}::{Norm(md.GetString(m.Name))}({string.Join(", ", sig.ParameterTypes)}) : {sig.ReturnType}";
                var body = new StringBuilder($"{m.Attributes} {m.ImplAttributes}");
                foreach (var ca in m.GetCustomAttributes()) body.Append("\n[" + names.Attribute(ca) + "]");
                foreach (var ph in m.GetParameters())
                {
                    var p = md.GetParameter(ph);
                    foreach (var ca in p.GetCustomAttributes()) body.Append($"\nparam {p.SequenceNumber} [{names.Attribute(ca)}]");
                }
                if (m.RelativeVirtualAddress != 0)
                {
                    var mb = pe.GetMethodBody(m.RelativeVirtualAddress);
                    body.Append($"\nmaxstack {mb.MaxStack} initlocals {mb.LocalVariablesInitialized}");
                    if (!mb.LocalSignature.IsNil)
                    {
                        var locals = md.GetStandaloneSignature(mb.LocalSignature).DecodeLocalSignature(names, null);
                        body.Append("\nlocals " + string.Join(", ", locals));
                    }
                    var (lines, offsetToIndex) = Disassemble(mb.GetILReader(), md, names);
                    foreach (var er in mb.ExceptionRegions)
                    {
                        string Ix(int off) => offsetToIndex.TryGetValue(off, out var i) ? i.ToString() : "@" + off;
                        body.Append($"\n.try {Ix(er.TryOffset)}..{Ix(er.TryOffset + er.TryLength)} {er.Kind} {Ix(er.HandlerOffset)}..{Ix(er.HandlerOffset + er.HandlerLength)}"
                            + (er.Kind == ExceptionRegionKind.Catch ? " " + names.Type(er.CatchType) : "")
                            + (er.Kind == ExceptionRegionKind.Filter ? " filter " + Ix(er.FilterOffset) : ""));
                    }
                    foreach (var l in lines) body.Append("\n" + l);
                }
                recs.Add(new Rec(key, body.ToString()));
            }
        }
        return new Dumped(recs, info);
    }

    static (List<string>, Dictionary<int, int>) Disassemble(BlobReader il, MetadataReader md, Names names)
    {
        var starts = new List<int>();
        var raw = new List<(OpCode op, object operand, int next)>();
        while (il.RemainingBytes > 0)
        {
            starts.Add(il.Offset);
            ushort v = il.ReadByte();
            if (v == 0xFE) v = (ushort)(0xFE00 | il.ReadByte());
            var op = Ops[v];
            object operand = null;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget: operand = (int)il.ReadSByte(); break;
                case OperandType.InlineBrTarget: operand = il.ReadInt32(); break;
                case OperandType.ShortInlineI: operand = op == OpCodes.Ldc_I4_S ? (object)(int)il.ReadSByte() : il.ReadByte(); break;
                case OperandType.ShortInlineVar: operand = (int)il.ReadByte(); break;
                case OperandType.InlineVar: operand = (int)il.ReadUInt16(); break;
                case OperandType.InlineI: operand = il.ReadInt32(); break;
                case OperandType.InlineI8: operand = il.ReadInt64(); break;
                case OperandType.ShortInlineR: operand = il.ReadSingle(); break;
                case OperandType.InlineR: operand = il.ReadDouble(); break;
                case OperandType.InlineString:
                    operand = "\"" + md.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32())).Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
                    break;
                case OperandType.InlineSwitch:
                    var count = il.ReadInt32(); var targets = new int[count];
                    for (var i = 0; i < count; i++) targets[i] = il.ReadInt32();
                    operand = targets; break;
                case OperandType.InlineSig: il.ReadInt32(); operand = "sig"; break;
                default: operand = names.Token(MetadataTokens.EntityHandle(il.ReadInt32())); break;
            }
            raw.Add((op, operand, il.Offset));
        }
        var index = new Dictionary<int, int>();
        for (var i = 0; i < starts.Count; i++) index[starts[i]] = i;
        index[il.Offset] = starts.Count;
        string Target(int next, int delta) => index.TryGetValue(next + delta, out var t) ? "L" + t : "L@" + (next + delta);
        var lines = new List<string>();
        for (var i = 0; i < raw.Count; i++)
        {
            var (op, operand, next) = raw[i];
            var text = op.OperandType switch
            {
                OperandType.ShortInlineBrTarget or OperandType.InlineBrTarget => Target(next, (int)operand),
                OperandType.InlineSwitch => string.Join(",", ((int[])operand).Select(d => Target(next, d))),
                _ => operand?.ToString() ?? ""
            };
            lines.Add($"{i}: {op.Name} {text}".TrimEnd());
        }
        return (lines, index);
    }

    sealed class Names : ISignatureTypeProvider<string, object>, ICustomAttributeTypeProvider<string>
    {
        readonly MetadataReader md;
        public Names(MetadataReader md) { this.md = md; }

        public string TypeDef(TypeDefinitionHandle h)
        {
            var t = md.GetTypeDefinition(h);
            var name = md.GetString(t.Name);
            var decl = t.GetDeclaringType();
            if (!decl.IsNil) return TypeDef(decl) + "/" + Norm(name);
            var ns = md.GetString(t.Namespace);
            return Norm(ns.Length > 0 ? ns + "." + name : name);
        }

        public string TypeRef(TypeReferenceHandle h)
        {
            var t = md.GetTypeReference(h);
            var name = md.GetString(t.Name);
            if (t.ResolutionScope.Kind == HandleKind.TypeReference) return TypeRef((TypeReferenceHandle)t.ResolutionScope) + "/" + name;
            var ns = md.GetString(t.Namespace);
            return ns.Length > 0 ? ns + "." + name : name;
        }

        public string Type(EntityHandle h) => h.Kind switch
        {
            HandleKind.TypeDefinition => TypeDef((TypeDefinitionHandle)h),
            HandleKind.TypeReference => TypeRef((TypeReferenceHandle)h),
            HandleKind.TypeSpecification => md.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(this, null),
            _ => h.Kind.ToString()
        };

        public string Token(EntityHandle h)
        {
            switch (h.Kind)
            {
                case HandleKind.MethodDefinition:
                    {
                        var m = md.GetMethodDefinition((MethodDefinitionHandle)h);
                        var sig = m.DecodeSignature(this, null);
                        return $"{TypeDef(m.GetDeclaringType())}::{Norm(md.GetString(m.Name))}({string.Join(", ", sig.ParameterTypes)}) : {sig.ReturnType}";
                    }
                case HandleKind.FieldDefinition:
                    {
                        var f = md.GetFieldDefinition((FieldDefinitionHandle)h);
                        return $"{TypeDef(f.GetDeclaringType())}::{Norm(md.GetString(f.Name))}";
                    }
                case HandleKind.MemberReference:
                    {
                        var r = md.GetMemberReference((MemberReferenceHandle)h);
                        var parent = r.Parent.Kind == HandleKind.MethodDefinition ? Token(r.Parent) : Type(r.Parent);
                        var name = Norm(md.GetString(r.Name));
                        if (r.GetKind() == MemberReferenceKind.Method)
                        {
                            var sig = r.DecodeMethodSignature(this, null);
                            return $"{parent}::{name}({string.Join(", ", sig.ParameterTypes)}) : {sig.ReturnType}";
                        }
                        return $"{parent}::{name} : {r.DecodeFieldSignature(this, null)}";
                    }
                case HandleKind.MethodSpecification:
                    {
                        var s = md.GetMethodSpecification((MethodSpecificationHandle)h);
                        return Token(s.Method) + "<" + string.Join(",", s.DecodeSignature(this, null)) + ">";
                    }
                default: return Type(h);
            }
        }

        public string Attribute(CustomAttributeHandle h)
        {
            var ca = md.GetCustomAttribute(h);
            var ctor = Token(ca.Constructor);
            try
            {
                var v = ca.DecodeValue(this);
                var parts = v.FixedArguments.Select(a => Val(a.Value))
                    .Concat(v.NamedArguments.Select(n => n.Name + "=" + Val(n.Value)));
                return ctor + "(" + string.Join(", ", parts) + ")";
            }
            catch (Exception e)
            {
                return ctor + " blob " + Convert.ToHexString(md.GetBlobBytes(ca.Value)) + " (" + e.GetType().Name + ")";
            }
        }

        static string Val(object o) => o switch
        {
            null => "null",
            string s => "\"" + Norm(s) + "\"",
            ImmutableArray<CustomAttributeTypedArgument<string>> arr => "[" + string.Join(",", arr.Select(x => Val(x.Value))) + "]",
            _ => o.ToString()
        };

        public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
        public string GetByReferenceType(string e) => e + "&";
        public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
        public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
        public string GetGenericMethodParameter(object c, int i) => "!!" + i;
        public string GetGenericTypeParameter(object c, int i) => "!" + i;
        public string GetModifiedType(string m, string u, bool r) => u;
        public string GetPinnedType(string e) => e + " pinned";
        public string GetPointerType(string e) => e + "*";
        public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
        public string GetSZArrayType(string e) => e + "[]";
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => TypeDef(h);
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => TypeRef(h);
        public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
        public string GetSystemType() => "System.Type";
        public bool IsSystemType(string t) => t == "System.Type";
        public string GetTypeFromSerializedName(string n) => Norm(n);
        public PrimitiveTypeCode GetUnderlyingEnumType(string t) => PrimitiveTypeCode.Int32;
    }
}
