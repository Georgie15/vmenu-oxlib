// Run: dotnet run --project tests/menuapi-compat [path to MenuAPI.dll]
//
// The packaged MenuAPI.dll is a local rebuild of MenuAPI 3.2.2 plus
// tools/menuapi/skip-disabled-legacy-toggle.patch. FiveM's client Mono ships an
// older mscorlib than the .NET SDK assumes. A rebuild that references any type
// FiveM lacks loads, but vMenu's `dynamic` calls read every loaded assembly's
// attributes and throw TypeLoadException. On November 17, 2026 the SDK's
// RepositoryUrl AssemblyMetadataAttribute did exactly that: invisible peds on
// spawn and a mono-2.0-sgen.dll crash.
//
// Checks:
//  1. Assembly-level attributes are the stock MenuAPI 3.2.2 set (NuGet cache).
//  2. Every type and member referenced outside MenuAPI is also referenced by stock 3.2.2.
//  3. ProcessToggleMenuButton has the 0..402 toggle-key guard (the V-key fix).
//  4. If FiveM is installed here, every type referenced by MenuAPI.dll and
//     vMenuClient.net.dll exists in FiveM's own runtime assemblies.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

static class Program
{
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

    static string StockMenuApi()
    {
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrEmpty(packages))
        {
            packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        }
        var path = Path.Combine(packages, "menuapi.fivem", "3.2.2", "lib", "net452", "MenuAPI.dll");
        if (!File.Exists(path))
        {
            throw new Exception($"Stock MenuAPI 3.2.2 not found at {path}. Run `dotnet restore vMenu/vMenuClient.csproj` first.");
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

        public MethodBodyBlock Body(int rva) => pe.GetMethodBody(rva);

        public void Dispose() => pe.Dispose();

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

        public HashSet<string> ExternalMemberRefs()
        {
            var result = new HashSet<string>();
            foreach (var handle in Md.MemberReferences)
            {
                var mr = Md.GetMemberReference(handle);
                if (mr.Parent.Kind != HandleKind.TypeReference) continue;
                var parent = TypeRef((TypeReferenceHandle)mr.Parent);
                if (parent.Assembly == null) continue;
                var provider = new SignatureNames(this);
                var reader = Md.GetBlobReader(mr.Signature);
                var header = reader.ReadSignatureHeader();
                reader.Reset();
                var decoder = new SignatureDecoder<string, object>(provider, Md, null);
                string signature;
                if (header.Kind == SignatureKind.Field)
                {
                    signature = decoder.DecodeFieldSignature(ref reader);
                }
                else
                {
                    var method = decoder.DecodeMethodSignature(ref reader);
                    signature = method.ReturnType + "(" + string.Join(", ", method.ParameterTypes) + ")";
                }
                result.Add($"[{parent.Assembly}]{parent.Name}::{Md.GetString(mr.Name)} {signature}");
            }
            return result;
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
        public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(", ", typeArguments) + ">";
        public string GetGenericMethodParameter(object genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object genericContext, int index) => "!" + index;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    }

    // Type names defined in, or forwarded by, one runtime assembly.
    static HashSet<string> RuntimeTypes(string path)
    {
        var result = new HashSet<string>();
        using var pe = new PEReader(File.OpenRead(path));
        var md = pe.GetMetadataReader();
        foreach (var handle in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(handle);
            var name = md.GetString(td.Name);
            var current = td;
            while (!current.GetDeclaringType().IsNil)
            {
                current = md.GetTypeDefinition(current.GetDeclaringType());
                name = md.GetString(current.Name) + "/" + name;
            }
            result.Add(md.GetString(current.Namespace) + "." + name);
        }
        foreach (var handle in md.ExportedTypes)
        {
            var et = md.GetExportedType(handle);
            var name = md.GetString(et.Name);
            var current = et;
            while (current.Implementation.Kind == HandleKind.ExportedType)
            {
                current = md.GetExportedType((ExportedTypeHandle)current.Implementation);
                name = md.GetString(current.Name) + "/" + name;
            }
            result.Add(md.GetString(current.Namespace) + "." + name);
        }
        return result;
    }

    static bool HasToggleGuard(Image image)
    {
        // ldc.i4 402: the upper bound of the guard copied from MenuAPI 3.2.4.
        byte[] ldc402 = { 0x20, 0x92, 0x01, 0x00, 0x00 };
        foreach (var handle in image.Md.TypeDefinitions)
        {
            var type = image.Md.GetTypeDefinition(handle);
            if (!image.Md.GetString(type.Name).StartsWith("<ProcessToggleMenuButton>")) continue;
            foreach (var methodHandle in type.GetMethods())
            {
                var method = image.Md.GetMethodDefinition(methodHandle);
                if (image.Md.GetString(method.Name) != "MoveNext" || method.RelativeVirtualAddress == 0) continue;
                var il = image.Body(method.RelativeVirtualAddress).GetILBytes();
                for (int i = 0; i + ldc402.Length <= il.Length; i++)
                {
                    if (il.Skip(i).Take(ldc402.Length).SequenceEqual(ldc402)) return true;
                }
            }
        }
        return false;
    }

    static int Main(string[] args)
    {
        var root = RepoRoot();
        var menuApiPath = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(root, "build", "vMenu", "MenuAPI.dll");
        var clientPath = Path.Combine(root, "build", "vMenu", "vMenuClient.net.dll");
        Console.WriteLine($"MenuAPI under test: {menuApiPath}");

        using var stock = new Image(StockMenuApi());
        using var packaged = new Image(menuApiPath);

        var extraAttributes = packaged.AssemblyAttributeTypes().Except(stock.AssemblyAttributeTypes()).ToList();
        Check(extraAttributes.Count == 0,
            "assembly attributes match stock MenuAPI 3.2.2" +
            (extraAttributes.Count > 0 ? ": extra " + string.Join(", ", extraAttributes) : ""));

        var stockTypes = new HashSet<string>(stock.ExternalTypeRefs().Select(t => $"[{t.Assembly}]{t.Name}"));
        var newTypes = packaged.ExternalTypeRefs().Select(t => $"[{t.Assembly}]{t.Name}").Where(t => !stockTypes.Contains(t)).ToList();
        Check(newTypes.Count == 0,
            "no external type references beyond stock MenuAPI 3.2.2" +
            (newTypes.Count > 0 ? ": " + string.Join(", ", newTypes) : ""));

        var newMembers = packaged.ExternalMemberRefs().Except(stock.ExternalMemberRefs()).ToList();
        Check(newMembers.Count == 0,
            "no external member references beyond stock MenuAPI 3.2.2" +
            (newMembers.Count > 0 ? ": " + string.Join(", ", newMembers) : ""));

        Check(HasToggleGuard(packaged), "ProcessToggleMenuButton has the 0..402 toggle-key guard");
        Check(!HasToggleGuard(stock), "the guard check rejects stock MenuAPI 3.2.2");

        var fivemLib = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FiveM", "FiveM.app", "citizen", "clr2", "lib", "mono", "4.5");
        if (!Directory.Exists(fivemLib))
        {
            Console.WriteLine("SKIP FiveM runtime check: no FiveM install at " + fivemLib);
        }
        else
        {
            var runtime = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in new[] { menuApiPath, clientPath })
            {
                using var image = new Image(path);
                var missing = new List<string>();
                foreach (var (name, assembly) in image.ExternalTypeRefs())
                {
                    var file = Path.Combine(fivemLib, assembly + ".dll");
                    if (!File.Exists(file)) continue; // shipped with the resource, e.g. MenuAPI or Newtonsoft.Json
                    if (!runtime.TryGetValue(assembly, out var types)) runtime[assembly] = types = RuntimeTypes(file);
                    if (!types.Contains(name)) missing.Add($"[{assembly}]{name}");
                }
                Check(missing.Count == 0,
                    $"{Path.GetFileName(path)} only references types in FiveM's runtime" +
                    (missing.Count > 0 ? ": missing " + string.Join(", ", missing) : ""));
            }
        }

        Console.WriteLine(failures == 0 ? "All MenuAPI compatibility checks passed." : $"{failures} check(s) failed.");
        return failures == 0 ? 0 : 1;
    }
}
