// Run: dotnet run --project tests/menuapi-compat [path to MenuAPI.dll]
//
// The packaged MenuAPI.dll is MenuAPI 4.1.3, the version vMenu Enhanced uses, rebuilt from the
// v4.1.3 source plus tools/menuapi/checkbox-left-toggle-4.1.3.patch (left toggles a checkbox, as
// MenuAPI 3 did), without the SDK's RepositoryUrl attribute (docs/current-build.md has the command). FiveM's client Mono ships an older mscorlib than the .NET SDK assumes. A DLL that
// references a type FiveM lacks still loads, but vMenu's `dynamic` calls read every loaded
// assembly's attributes and throw TypeLoadException. On November 17, 2026 that attribute
// (System.Reflection.AssemblyMetadataAttribute, which FiveM's mscorlib does not have) caused
// invisible peds on spawn and a mono-2.0-sgen.dll crash. The NuGet build of MenuAPI 4.1.3 carries
// the same attribute, so it must not be shipped as it is.
//
// Checks:
//  1. The packaged DLL is MenuAPI 4.1.3, and vMenuClient.net.dll was built against 4.1.3.
//  2. Its assembly attributes are all types FiveM's client mscorlib has, from the fixed list below;
//     AssemblyMetadataAttribute in particular is absent.
//  3. Its code is the official release plus the patch. tools/ilcompare finds the same types, fields
//     and method bodies (operands resolved to names) as in the NuGet MenuAPI.FiveM 4.1.3 DLL, plus
//     exactly one new method, MenuCheckboxItem.GoLeft, which selects the checkbox. The only
//     assembly-level differences are the version strings and the removed attribute. It also uses
//     the same outside types and members, apart from that attribute.
//  4. If FiveM is installed here, every outside type and member that MenuAPI.dll and
//     vMenuClient.net.dll use exists in FiveM's own runtime assemblies, and so does every
//     attribute type on MenuAPI.dll.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

static class Program
{
    const string ExpectedVersion = "4.1.3.0";

    // Assembly attribute types FiveM's client mscorlib has (checked against FiveM's runtime below
    // when it is installed).
    static readonly HashSet<string> SafeAssemblyAttributes = new HashSet<string>
    {
        "System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
        "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
        "System.Diagnostics.DebuggableAttribute",
        "System.Runtime.Versioning.TargetFrameworkAttribute",
        "System.Reflection.AssemblyCompanyAttribute",
        "System.Reflection.AssemblyConfigurationAttribute",
        "System.Reflection.AssemblyCopyrightAttribute",
        "System.Reflection.AssemblyDescriptionAttribute",
        "System.Reflection.AssemblyFileVersionAttribute",
        "System.Reflection.AssemblyInformationalVersionAttribute",
        "System.Reflection.AssemblyProductAttribute",
        "System.Reflection.AssemblyTitleAttribute",
    };

    static int failures;

    static void Check(bool condition, string message)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + message);
        if (!condition) failures++;
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
        {
            dir = dir.Parent;
        }
        if (dir == null) throw new Exception("Run from inside the vMenu-ox repository.");
        return dir.FullName;
    }

    static string NuGetMenuApi()
    {
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrEmpty(packages))
        {
            packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        }
        var path = Path.Combine(packages, "menuapi.fivem", "4.1.3", "lib", "net452", "MenuAPI.dll");
        if (!File.Exists(path))
        {
            throw new Exception($"MenuAPI.FiveM 4.1.3 not found at {path}. Run `dotnet restore vMenu/vMenuClient.csproj` first.");
        }
        return path;
    }

    sealed class Image : IDisposable
    {
        readonly PEReader pe;
        public readonly MetadataReader Md;
        public readonly string Path;

        public Image(string path)
        {
            Path = path;
            pe = new PEReader(File.OpenRead(path));
            Md = pe.GetMetadataReader();
        }

        public void Dispose() => pe.Dispose();

        public string Version => Md.GetAssemblyDefinition().Version.ToString();

        public string ReferencedVersion(string assembly) =>
            Md.AssemblyReferences.Select(Md.GetAssemblyReference)
                .Where(r => Md.GetString(r.Name) == assembly)
                .Select(r => r.Version.ToString())
                .FirstOrDefault();

        // "Namespace.Outer/Inner" plus the assembly it resolves to, or null for local types.
        public (string Name, string Assembly) TypeRef(TypeReferenceHandle handle)
        {
            var tr = Md.GetTypeReference(handle);
            var name = Md.GetString(tr.Name);
            var scope = tr.ResolutionScope;
            while (scope.Kind == HandleKind.TypeReference)
            {
                var parent = Md.GetTypeReference((TypeReferenceHandle)scope);
                name = Md.GetString(parent.Name) + "/" + name;
                tr = parent;
                scope = parent.ResolutionScope;
            }
            name = Md.GetString(tr.Namespace) + "." + name;
            var assembly = scope.Kind == HandleKind.AssemblyReference
                ? Md.GetString(Md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name)
                : null;
            return (name, assembly);
        }

        public IEnumerable<(string Name, string Assembly)> ExternalTypeRefs() =>
            Md.TypeReferences.Select(TypeRef).Where(t => t.Assembly != null);

        // The outside type a member reference belongs to, looking through generic instantiations.
        public (string Name, string Assembly)? MemberParent(MemberReference mr)
        {
            if (mr.Parent.Kind == HandleKind.TypeReference)
            {
                var t = TypeRef((TypeReferenceHandle)mr.Parent);
                return t.Assembly == null ? null : t;
            }
            if (mr.Parent.Kind == HandleKind.TypeSpecification)
            {
                var spec = Md.GetTypeSpecification((TypeSpecificationHandle)mr.Parent);
                var generic = spec.DecodeSignature(new GenericTypeOf(), null);
                if (generic is TypeReferenceHandle handle)
                {
                    var t = TypeRef(handle);
                    return t.Assembly == null ? null : t;
                }
            }
            return null;
        }

        // [assembly]Type::member signature, for every outside member this DLL uses.
        public HashSet<string> ExternalMemberRefs()
        {
            var result = new HashSet<string>();
            foreach (var handle in Md.MemberReferences)
            {
                var mr = Md.GetMemberReference(handle);
                var parent = MemberParent(mr);
                if (parent == null) continue;
                result.Add($"[{parent.Value.Assembly}]{parent.Value.Name}::{Md.GetString(mr.Name)} {Signature(mr)}");
            }
            return result;
        }

        public IEnumerable<(string Assembly, string Type, string Member)> ExternalMembers()
        {
            foreach (var handle in Md.MemberReferences)
            {
                var mr = Md.GetMemberReference(handle);
                var parent = MemberParent(mr);
                if (parent == null) continue;
                yield return (parent.Value.Assembly, parent.Value.Name, Md.GetString(mr.Name));
            }
        }

        string Signature(MemberReference mr)
        {
            var provider = new SignatureNames(this);
            var reader = Md.GetBlobReader(mr.Signature);
            var header = reader.ReadSignatureHeader();
            reader.Reset();
            var decoder = new SignatureDecoder<string, object>(provider, Md, null);
            if (header.Kind == SignatureKind.Field)
            {
                return decoder.DecodeFieldSignature(ref reader);
            }
            var method = decoder.DecodeMethodSignature(ref reader);
            return method.ReturnType + "(" + string.Join(", ", method.ParameterTypes) + ")";
        }

        public HashSet<string> AssemblyAttributeTypes()
        {
            var result = new HashSet<string>();
            foreach (var handle in Md.GetAssemblyDefinition().GetCustomAttributes())
            {
                var ctor = Md.GetCustomAttribute(handle).Constructor;
                if (ctor.Kind == HandleKind.MemberReference)
                {
                    var parent = Md.GetMemberReference((MemberReferenceHandle)ctor).Parent;
                    if (parent.Kind == HandleKind.TypeReference)
                    {
                        result.Add(TypeRef((TypeReferenceHandle)parent).Name);
                        continue;
                    }
                }
                result.Add("<local attribute>");
            }
            return result;
        }
    }

    // Decodes a generic instantiation's TypeSpec to the generic type's reference.
    sealed class GenericTypeOf : ISignatureTypeProvider<object, object>
    {
        public object GetGenericInstantiation(object genericType, ImmutableArray<object> typeArguments) => genericType;
        public object GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => handle;
        public object GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => handle;
        public object GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => null;
        public object GetPrimitiveType(PrimitiveTypeCode typeCode) => null;
        public object GetSZArrayType(object elementType) => null;
        public object GetArrayType(object elementType, ArrayShape shape) => null;
        public object GetByReferenceType(object elementType) => null;
        public object GetPointerType(object elementType) => null;
        public object GetPinnedType(object elementType) => null;
        public object GetGenericMethodParameter(object genericContext, int index) => null;
        public object GetGenericTypeParameter(object genericContext, int index) => null;
        public object GetFunctionPointerType(MethodSignature<object> signature) => null;
        public object GetModifiedType(object modifier, object unmodifiedType, bool isRequired) => unmodifiedType;
    }

    // Renders signatures with type names, so token numbering does not matter.
    sealed class SignatureNames : ISignatureTypeProvider<string, object>
    {
        readonly Image image;

        public SignatureNames(Image image) => this.image = image;

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var td = reader.GetTypeDefinition(handle);
            return reader.GetString(td.Namespace) + "." + reader.GetString(td.Name);
        }
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var (name, assembly) = image.TypeRef(handle);
            return assembly == null ? name : $"[{assembly}]{name}";
        }
        public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPinnedType(string elementType) => elementType + " pinned";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(", ", typeArguments) + ">";
        public string GetGenericMethodParameter(object genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object genericContext, int index) => "!" + index;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    }

    // FiveM's runtime assemblies, read once each.
    sealed class Runtime
    {
        readonly string dir;
        readonly Dictionary<string, (PEReader Pe, MetadataReader Md)> loaded = new(StringComparer.OrdinalIgnoreCase);

        public Runtime(string dir) => this.dir = dir;

        public bool Has(string assembly) => File.Exists(Path.Combine(dir, assembly + ".dll"));

        MetadataReader Md(string assembly)
        {
            if (!loaded.TryGetValue(assembly, out var entry))
            {
                var pe = new PEReader(File.OpenRead(Path.Combine(dir, assembly + ".dll")));
                loaded[assembly] = entry = (pe, pe.GetMetadataReader());
            }
            return entry.Md;
        }

        static string FullName(MetadataReader md, TypeDefinition td)
        {
            var name = md.GetString(td.Name);
            var current = td;
            while (!current.GetDeclaringType().IsNil)
            {
                current = md.GetTypeDefinition(current.GetDeclaringType());
                name = md.GetString(current.Name) + "/" + name;
            }
            return md.GetString(current.Namespace) + "." + name;
        }

        // The assembly and definition of a type, following type forwarders.
        (MetadataReader Md, TypeDefinition Type)? Find(string assembly, string typeName, int depth = 0)
        {
            if (depth > 4 || !Has(assembly)) return null;
            var md = Md(assembly);
            foreach (var handle in md.TypeDefinitions)
            {
                var td = md.GetTypeDefinition(handle);
                if (FullName(md, td) == typeName) return (md, td);
            }
            // Forwarded types. A nested type is forwarded through its outermost type's entry.
            foreach (var handle in md.ExportedTypes)
            {
                var et = md.GetExportedType(handle);
                var name = md.GetString(et.Name);
                var outer = et;
                while (outer.Implementation.Kind == HandleKind.ExportedType)
                {
                    outer = md.GetExportedType((ExportedTypeHandle)outer.Implementation);
                    name = md.GetString(outer.Name) + "/" + name;
                }
                if (md.GetString(outer.Namespace) + "." + name != typeName) continue;
                if (outer.Implementation.Kind == HandleKind.AssemblyReference)
                {
                    var target = md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)outer.Implementation).Name);
                    return Find(target, typeName, depth + 1);
                }
            }
            return null;
        }

        public bool HasType(string assembly, string typeName) => Find(assembly, typeName) != null;

        // Whether the type, or a type it derives from, defines a method or field with this name.
        public bool HasMember(string assembly, string typeName, string member, int depth = 0)
        {
            if (depth > 8) return false;
            var found = Find(assembly, typeName);
            if (found == null) return false;
            var (md, td) = found.Value;
            if (td.GetMethods().Any(m => md.GetString(md.GetMethodDefinition(m).Name) == member)) return true;
            if (td.GetFields().Any(f => md.GetString(md.GetFieldDefinition(f).Name) == member)) return true;
            if (td.BaseType.Kind == HandleKind.TypeReference)
            {
                var tr = md.GetTypeReference((TypeReferenceHandle)td.BaseType);
                if (tr.ResolutionScope.Kind == HandleKind.AssemblyReference)
                {
                    var baseAssembly = md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope).Name);
                    return HasMember(baseAssembly, md.GetString(tr.Namespace) + "." + md.GetString(tr.Name), member, depth + 1);
                }
            }
            else if (td.BaseType.Kind == HandleKind.TypeDefinition)
            {
                var baseType = md.GetTypeDefinition((TypeDefinitionHandle)td.BaseType);
                return HasMember(assembly, FullName(md, baseType), member, depth + 1);
            }
            return false;
        }
    }

    static (int Exit, string Output) Run(string file, params string[] arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(info);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    static int Main(string[] args)
    {
        var root = RepoRoot();
        var menuApiPath = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(root, "build", "vMenu", "MenuAPI.dll");
        var clientPath = Path.Combine(root, "build", "vMenu", "vMenuClient.net.dll");
        Console.WriteLine($"MenuAPI under test: {menuApiPath}");

        using var official = new Image(NuGetMenuApi());
        using var packaged = new Image(menuApiPath);
        using var client = new Image(clientPath);

        // 1. Versions.
        Check(packaged.Version == ExpectedVersion, $"the packaged MenuAPI is {ExpectedVersion} (found {packaged.Version})");
        Check(client.ReferencedVersion("MenuAPI") == ExpectedVersion,
            $"vMenuClient.net.dll was built against MenuAPI {ExpectedVersion} (found {client.ReferencedVersion("MenuAPI") ?? "none"})");

        // 2. Assembly attributes.
        var attributes = packaged.AssemblyAttributeTypes();
        var unsafeAttributes = attributes.Where(a => !SafeAssemblyAttributes.Contains(a)).ToList();
        Check(unsafeAttributes.Count == 0,
            "assembly attributes are all types FiveM's mscorlib has" +
            (unsafeAttributes.Count > 0 ? ": also " + string.Join(", ", unsafeAttributes) : ""));
        Check(!attributes.Contains("System.Reflection.AssemblyMetadataAttribute"), "no AssemblyMetadataAttribute (the November 17 crash)");
        Check(official.AssemblyAttributeTypes().Contains("System.Reflection.AssemblyMetadataAttribute"),
            "the attribute check rejects the NuGet DLL, which carries AssemblyMetadataAttribute");

        // 3. Same code as the official release.
        var report = Path.Combine(Path.GetTempPath(), "menuapi-compat-ilcompare.txt");
        File.Delete(report); // never read a previous run's report
        var (exit, output) = Run("dotnet", "run", "--project", Path.Combine(root, "tools", "ilcompare"), "-c", "Release", "--",
            official.Path, packaged.Path, report);
        var reportText = File.Exists(report) ? File.ReadAllText(report) : output;
        var records = reportText.Split('\n').Where(l => l.StartsWith("--- ")).Select(l => l.Trim()).ToList();
        Check(reportText.Contains("== Type/field/method records only in A: 0, only in B: 1")
              && records.Count == 1
              && records[0].StartsWith("--- METHOD MenuAPI.MenuCheckboxItem::GoLeft() : Void"),
            "tools/ilcompare: the official MenuAPI 4.1.3 plus only MenuCheckboxItem.GoLeft" +
            (records.Count == 1 ? "" : $" (exit {exit}; {records.Count} differing records; see {report})"));
        Check(reportText.Contains("call MenuAPI.Menu::SelectItem(MenuAPI.MenuItem) : Void")
              && reportText.Contains("call MenuAPI.MenuItem::GoLeft() : Void"),
            "MenuCheckboxItem.GoLeft selects an enabled checkbox and otherwise does what any row does");
        var expectedAssemblyLines = new[]
        {
            "AssemblyFileVersionAttribute", "AssemblyInformationalVersionAttribute",
            "typedefs 129 methoddefs 941 fielddefs 630", "typedefs 129 methoddefs 942 fielddefs 630",
            "AssemblyMetadataAttribute::.ctor(String, String) : Void(\"RepositoryUrl\"",
        };
        var assemblyLines = reportText.Split('\n')
            .Where(l => l.TrimStart().StartsWith("A only:") || l.TrimStart().StartsWith("B only:"))
            .Where(l => !l.Contains("Type/field/method"))
            .ToList();
        var unexpectedAssemblyLines = assemblyLines.Where(l => !expectedAssemblyLines.Any(l.Contains)).ToList();
        Check(unexpectedAssemblyLines.Count == 0,
            "assembly-level differences are only the version strings, the removed RepositoryUrl attribute and one more method" +
            (unexpectedAssemblyLines.Count > 0 ? ": " + string.Join(" | ", unexpectedAssemblyLines.Select(l => l.Trim())) : ""));
        const string removedAttribute = "[mscorlib]System.Reflection.AssemblyMetadataAttribute";
        var officialTypes = new HashSet<string>(official.ExternalTypeRefs().Select(t => $"[{t.Assembly}]{t.Name}"));
        officialTypes.Remove(removedAttribute);
        var packagedTypes = new HashSet<string>(packaged.ExternalTypeRefs().Select(t => $"[{t.Assembly}]{t.Name}"));
        var typeDiff = packagedTypes.Except(officialTypes).Concat(officialTypes.Except(packagedTypes)).ToList();
        Check(typeDiff.Count == 0, "outside types used match the official MenuAPI 4.1.3, apart from the removed attribute" +
            (typeDiff.Count > 0 ? ": " + string.Join(", ", typeDiff) : ""));
        var officialMembers = official.ExternalMemberRefs();
        officialMembers.RemoveWhere(m => m.StartsWith(removedAttribute + "::"));
        var packagedMembers = packaged.ExternalMemberRefs();
        var memberDiff = packagedMembers.Except(officialMembers).Concat(officialMembers.Except(packagedMembers)).ToList();
        Check(memberDiff.Count == 0, "outside members used match the official MenuAPI 4.1.3, apart from the removed attribute" +
            (memberDiff.Count > 0 ? ": " + string.Join(", ", memberDiff.Take(10)) : ""));

        // 4. FiveM's own runtime.
        var fivemLib = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FiveM", "FiveM.app", "citizen", "clr2", "lib", "mono", "4.5");
        if (!Directory.Exists(fivemLib))
        {
            Console.WriteLine("SKIP FiveM runtime checks: no FiveM install at " + fivemLib);
        }
        else
        {
            var runtime = new Runtime(fivemLib);
            foreach (var image in new[] { packaged, client })
            {
                var name = Path.GetFileName(image.Path);
                // References to assemblies FiveM doesn't have (MenuAPI, Newtonsoft.Json) ship with the resource.
                var missingTypes = image.ExternalTypeRefs()
                    .Where(t => runtime.Has(t.Assembly) && !runtime.HasType(t.Assembly, t.Name))
                    .Select(t => $"[{t.Assembly}]{t.Name}").Distinct().ToList();
                Check(missingTypes.Count == 0, $"{name} only uses types FiveM's runtime has" +
                    (missingTypes.Count > 0 ? ": missing " + string.Join(", ", missingTypes) : ""));
                var members = image.ExternalMembers().Where(m => runtime.Has(m.Assembly)).Distinct().ToList();
                var missingMembers = members
                    .Where(m => runtime.HasType(m.Assembly, m.Type) && !runtime.HasMember(m.Assembly, m.Type, m.Member))
                    .Select(m => $"[{m.Assembly}]{m.Type}::{m.Member}").ToList();
                Check(missingMembers.Count == 0, $"{name} only uses members FiveM's runtime has ({members.Count} checked)" +
                    (missingMembers.Count > 0 ? ": missing " + string.Join(", ", missingMembers) : ""));
            }
            var missingAttributes = SafeAssemblyAttributes.Where(a => !runtime.HasType("mscorlib", a)).ToList();
            Check(missingAttributes.Count == 0, "FiveM's mscorlib has every attribute type on the safe list" +
                (missingAttributes.Count > 0 ? ": missing " + string.Join(", ", missingAttributes) : ""));
            Check(!runtime.HasType("mscorlib", "System.Reflection.AssemblyMetadataAttribute"),
                "FiveM's mscorlib still lacks AssemblyMetadataAttribute (why the NuGet DLL can't ship)");
        }

        Console.WriteLine(failures == 0 ? "All MenuAPI compatibility checks passed." : $"{failures} check(s) failed.");
        return failures == 0 ? 0 : 1;
    }
}
