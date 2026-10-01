using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal static partial class Program
{
    private static void CheckAssembly(string path)
    {
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream); var m = pe.GetMetadataReader();
        string assemblyName = m.GetString(m.GetAssemblyDefinition().Name);
        string policyPath = Path.Combine(repositoryRoot, "tests", "AIUsage.OfflineTests", "allowed-api", assemblyName + ".txt");
        var allowed = File.ReadAllLines(policyPath).Where(l => !l.StartsWith('#')).ToHashSet(StringComparer.Ordinal);
        var unreviewed = m.AssemblyReferences.Select(h => "A " + m.GetString(m.GetAssemblyReference(h).Name)).Where(n => !allowed.Contains(n)).ToList();
        string FullName(TypeReferenceHandle h)
        {
            var t = m.GetTypeReference(h);
            return t.ResolutionScope.Kind == HandleKind.TypeReference ? FullName((TypeReferenceHandle)t.ResolutionScope) + "/" + m.GetString(t.Name) : (m.GetString(t.Namespace).Length > 0 ? m.GetString(t.Namespace) + "." : "") + m.GetString(t.Name);
        }
        unreviewed.AddRange(m.TypeReferences.Select(h => "T " + FullName(h)).Where(n => !allowed.Contains(n)));
        Require(unreviewed.Count == 0, "Unreviewed references in " + assemblyName + ": " + string.Join(" | ", unreviewed));
        foreach (var handle in m.TypeReferences)
        {
            var type = m.GetTypeReference(handle); string ns = m.GetString(type.Namespace), name = m.GetString(type.Name);
            Require(!(ns == "System.Net" || ns.StartsWith("System.Net.", StringComparison.Ordinal)), "Network type: " + ns + "." + name);
            Require(!(ns == "System.Diagnostics" && name.StartsWith("Process", StringComparison.Ordinal)), "Process API: " + name);
            Require(!(ns == "Microsoft.Win32" && (name.StartsWith("Registry") || name.EndsWith("FileDialog"))), "Credential/shell API: " + name);
            Require(name is not ("WebBrowser" or "Hyperlink" or "FolderBrowserDialog"), "External navigation: " + name);
        }
        foreach (var handle in m.MethodDefinitions)
        {
            var method = m.GetMethodDefinition(handle); if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0) continue;
            var import = method.GetImport(); Equal("user32.dll", m.GetString(m.GetModuleReference(import.Module).Name).ToLowerInvariant()); Equal("DestroyIcon", m.GetString(import.Name));
        }
        foreach (var handle in m.MemberReferences)
        {
            var member = m.GetMemberReference(handle); string name = m.GetString(member.Name); if (member.Parent.Kind != HandleKind.TypeReference) continue;
            var type = m.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (m.GetString(type.Namespace) == "System.Reflection" && m.GetString(type.Name) == "Assembly") Require(!name.StartsWith("Load", StringComparison.Ordinal) && name is not ("GetType" or "CreateInstance"), "Dynamic assembly loading/activation is not permitted");
            string full = FullName((TypeReferenceHandle)member.Parent);
            if (full == "System.Windows.Markup.XamlReader") Equal("Parse", name);
            Require(!(full == "System.Type" && name is "GetType" or "InvokeMember"), "Dynamic type lookup is not permitted");
            Require(!(full == "System.Runtime.InteropServices.Marshal" && (name.Contains("DelegateForFunctionPointer") || name.Contains("GetFunctionPointerForDelegate"))), "Dynamic native invocation is not permitted");
        }
    }
}
